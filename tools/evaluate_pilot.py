"""Pre-registered BLE pilot evaluation: constraints / cost / continuity / side effects.

Consumes immutable version-1 logs; never replays a solver or replaces displayed poses.
Python standard library only. Computations in KeyboardRootLocal metres, tables in mm.
"""
import argparse
import datetime as dt
import hashlib
import html
import json
import math
import statistics as stats
import sys
from collections import defaultdict
from pathlib import Path

import analyze_rule_effect as old
from analyze_raw_session import point, inverse

METHODS = ('Raw','Legacy','FullRules')


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()


def utc(value):
    parsed=dt.datetime.fromisoformat(value.replace('Z','+00:00'))
    old.require(parsed.tzinfo is not None,'UTC offset required')
    return parsed.astimezone(dt.timezone.utc)


def dump(path,value):
    path.write_text(json.dumps(value,indent=2,ensure_ascii=False,allow_nan=False),encoding='utf-8')


def lock_protocol(protocol, output):
    source=json.loads(Path(protocol).read_text(encoding='utf-8-sig'))
    validate_protocol(source)
    output=Path(output)
    old.require(not output.exists(),'Protocol lock already exists; never overwrite a preregistration')
    output.parent.mkdir(parents=True,exist_ok=True)
    dump(output,dict(schema_version=1,locked_at_utc=dt.datetime.now(dt.timezone.utc).isoformat(),
                     protocol=source,protocol_sha256=hashlib.sha256(canonical(source)).hexdigest()))


def init_study(output):
    output=Path(output).resolve()
    old.require(not output.exists(),'Study directory already exists')
    output.mkdir(parents=True)
    docs=Path(__file__).parent.parent/'docs'
    (output/'protocol.json').write_bytes((docs/'pilot_protocol.defaults.json').read_bytes())
    lock_protocol(output/'protocol.json',output/'protocol.lock.json')
    lock=json.loads((output/'protocol.lock.json').read_text())
    config=json.loads((docs/'pilot_conditions.example.json').read_text())
    config.update(protocol_sha256=lock['protocol_sha256'],trials=[])
    dump(output/'conditions.json',config)
    dump(output/'calibration_verification.json',dict(points=[]))
    dump(output/'synchronization.json',dict(sessions=[]))


def validate_protocol(p):
    old.require(p['schema_version']==1 and p['midi_route']=='BLE','Pilot route must be BLE')
    required=('max_gap_s','min_dt_s','approach_s','release_window_s','ratio_min_improvement_mm',
              'bone_change_tolerance_mm','angle_tolerance_deg','geometry_epsilon_mm','abrupt_step_mm',
              'abrupt_offset_speed_mm_s','release_raw_tolerance_mm','release_stable_s',
              'min_valid_rate','max_stopped_rate','min_application_rate','max_calibration_p95_mm',
              'max_sync_residual_p95_ms','max_sync_uncertainty_ms','max_clock_drift_ppm','min_anchor_span_s','min_calibration_points')
    old.require(all(old.finite(p[k])>0 for k in required),'Protocol thresholds must be finite and positive')
    old.require(p['min_dt_s'] < p['max_gap_s'],'Invalid derivative sampling bounds')
    old.require(all(0<p[k]<=1 for k in ('min_valid_rate','max_stopped_rate','min_application_rate')),'Invalid rate threshold')
    old.require(p['required_trials']==5,'This pilot gate requires exactly five trials')
    old.require(isinstance(p['min_calibration_points'],int) and p['min_calibration_points']>=4,'At least four held-out points required')


def require_ble(data):
    old.require(all(e['source']=='BLE' for e in data['events']),'UDP or mixed MIDI routes are ineligible for this BLE pilot')


def midi_audit(data, start, end):
    """Require real paired On/Off; no synthetic reset counts as successful release."""
    held,spans,issues={},[],[]
    last_generation={}
    for e in data['events']:
        t=old.finite(e['quest_observed_timestamp_sec'])
        source,generation,index=old.event_key(e)
        if source in last_generation and generation!=last_generation[source] or old.truth(e['synthetic']):
            for k,(on,begin) in list(held.items()):
                if k[0]==source:
                    if begin<end and t>start:
                        issues.append('disconnect_or_generation_reset_during_key_down')
                    spans.append(dict(event=on,start=begin,end=t,off=None))
                    del held[k]
        last_generation[source]=generation
        if old.truth(e['synthetic']):
            continue
        k=(source,generation,int(e['channel']),int(e['note']))
        on=e['event_type']=='NoteOn' and int(e['velocity'])>0
        off=e['event_type']=='NoteOff' or e['event_type']=='NoteOn' and int(e['velocity'])==0
        if on:
            if k in held:
                prev,begin=held.pop(k)
                if start<=t<end:
                    issues.append('retrigger_without_note_off')
                spans.append(dict(event=prev,start=begin,end=t,off=None))
            held[k]=(e,t)
        elif off:
            if k not in held:
                if start<=t<end:
                    issues.append('unmatched_note_off')
            else:
                event,begin=held.pop(k)
                spans.append(dict(event=event,start=begin,end=t,off=e))
        elif e['event_type']=='ControlChange' and int(e['control']) in (120,123):
            for key,(event,begin) in list(held.items()):
                if key[0]==source and key[2]==int(e['channel']):
                    if begin<end and t>start:
                        issues.append('control_reset_instead_of_note_off')
                    spans.append(dict(event=event,start=begin,end=t,off=None))
                    del held[key]
    for event,begin in held.values():
        if begin<end:
            if begin>=start:
                issues.append('missing_note_off')
            spans.append(dict(event=event,start=begin,end=end,off=None))
    selected=[s for s in spans if start<=s['start']<end]
    if any(s['start']<start<s['end'] for s in spans):
        issues.append('trial_begins_with_key_already_down')
    if any(s['end']>=end for s in selected):
        issues.append('trial_has_no_recorded_release_tail')
    return selected,sorted(set(issues))


def check_pair(data, trial, raw, pair):
    # Reuse the established coordinate validator with the actual logged path.
    method=pair['requested_method']
    old.require(method==pair['executed_method'] and method in METHODS,'Remote/ablation/mismatched display method')
    expected=trial.get('logged_method',trial['method'])
    old.require(method==expected,'Unexpected logged correction method')
    if trial['method']!='Raw':
        old.require(method==trial['method'],'Condition/method mismatch')
    revision=int(pair['settings_revision'])
    settings=data['settings'][revision]
    old.require(settings['configuration_valid'] is True,'Invalid logged settings')
    common=settings['common']
    if trial['method']=='Raw':
        old.require(not common['enabled'] or method=='Raw','Raw condition must actually disable correction')
    old.check_pair(raw,pair,trial,expected_method=method)


def sub(a,b):
    return tuple(x-y for x,y in zip(a,b))


def norm(a):
    return math.sqrt(sum(x*x for x in a))


def segment_distance(p,q,r,s):
    """Closest distance of finite segments, including degenerate segments."""
    u,v,w=sub(q,p),sub(s,r),sub(p,r)
    dot=lambda a,b:sum(x*y for x,y in zip(a,b))
    a,b,c,d,e=dot(u,u),dot(u,v),dot(v,v),dot(u,w),dot(v,w)
    clamp=lambda x:max(0.,min(1.,x))
    if a<1e-20 and c<1e-20:
        return norm(w)
    if a<1e-20:
        t,h=0.,clamp(e/c)
    elif c<1e-20:
        t,h=clamp(-d/a),0.
    else:
        denominator=a*c-b*b
        t=clamp((b*e-c*d)/denominator) if denominator>1e-20 else 0.
        h=(b*t+e)/c
        if h<0:
            h,t=0.,clamp(-d/a)
        elif h>1:
            h,t=1.,clamp((b-d)/a)
    return norm(tuple(w[i]+t*u[i]-h*v[i] for i in range(3)))


def chain(finger):
    return ([finger+'Metacarpal',finger+'Proximal',finger+'Distal',finger+'Tip'] if finger=='Thumb' else
            [finger+'Proximal',finger+'Intermediate',finger+'Distal',finger+'Tip'])


def angle(a,b):
    length=norm(a)*norm(b)
    return math.degrees(math.acos(max(-1.,min(1.,sum(x*y for x,y in zip(a,b))/length)))) if length>1e-12 else None


def penetration(points, keys, radius, permitted):
    maximum=0.
    for a,b in zip(points,points[1:]):
        for f in (0.,.5,1.):
            p=tuple(x+(y-x)*f for x,y in zip(a,b))
            for key in keys:
                lo,hi=old.xyz(key['min']),old.xyz(key['max'])
                x,z=max(lo[0]-p[0],p[0]-hi[0],0.),max(lo[2]-p[2],p[2]-hi[2],0.)
                squared=x*x+z*z
                if squared<=radius*radius:
                    maximum=max(maximum,hi[1]+math.sqrt(max(0.,radius*radius-squared))-permitted-p[1])
    return maximum


def derivative_step(previous, t, sid, positions, protocol):
    """Nonuniform finite differences; derivatives carry their own midpoint times."""
    now=dict(t=t,sid=sid,positions=positions)
    values={}
    if previous is None or previous['sid']!=sid:
        return now,values
    elapsed=t-previous['t']
    if not protocol['min_dt_s']<=elapsed<=protocol['max_gap_s']:
        return now,values
    values['dt_ms']=elapsed*1000
    for label,p in positions.items():
        delta=sub(p,previous['positions'][label])
        values[label+'_step_mm']=norm(delta)*1000
        velocity=tuple(x/elapsed for x in delta)
        vt=(t+previous['t'])/2
        now[label+'_v']=(vt,velocity)
        values[label+'_speed_mm_s']=norm(velocity)*1000
        if label+'_v' in previous:
            previous_t,previous_v=previous[label+'_v']
            acceleration=tuple(x/(vt-previous_t) for x in sub(velocity,previous_v))
            at=(vt+previous_t)/2
            now[label+'_a']=(at,acceleration)
            values[label+'_acceleration_mm_s2']=norm(acceleration)*1000
            if label+'_a' in previous:
                previous_t,previous_a=previous[label+'_a']
                jerk=tuple(x/(at-previous_t) for x in sub(acceleration,previous_a))
                values[label+'_jerk_mm_s3']=norm(jerk)*1000
    return now,values


def phase_at(t, spans, finger, protocol, common):
    relevant=[s for s in spans if s['assignment']['finger']==finger]
    active=[s for s in relevant if s['start']<=t<s['end']]
    if active:
        return ('engage' if t-active[0]['start']<common['engageSeconds'] else 'hold'),active,True
    previous=[s for s in relevant if s['end']<=t and t-s['end']<=protocol['release_window_s']]
    if previous:
        return 'release',[max(previous,key=lambda s:s['end'])],False
    future=[s for s in relevant if 0<s['start']-t<=protocol['approach_s']]
    if future:
        return 'approach',[min(future,key=lambda s:s['start'])],False
    return 'nonpressing',[],False


def metric_rows(data,trial,protocol):
    start,end=old.finite(trial['start_sec']),old.finite(trial['end_sec'])
    old.require(start<end and trial['method'] in METHODS,'Invalid trial/method')
    old.require(trial['video_verified'] is True and trial['calibration_verified'] is True,'Video and calibration confirmation required')
    old.require(trial['task'] in old.TASKS,'Unknown task')
    times=[r['timestamp'] for r in data['callbacks'].values()]
    old.require(min(times)<=start<end<=max(times),'Trial outside recorded callback interval')
    settings=data['settings'][int(trial['settings_revision'])]
    old.require(old.finite(settings['quest_timestamp_sec'])<=start,'Future settings')
    common,rule=settings['common'],settings['rule']
    hand='Left' if common['leftHand'] else 'Right'
    old.require(trial['hand']==hand,'Wrong hand')
    spans,issues=midi_audit(data,start,end)
    old.require(spans,'No Note On in trial')
    assignments=old.unique(trial['assignments'],lambda a:(a['source'],int(a['generation']),int(a['onset_event_index'])))
    for s in spans:
        e=s['event']
        old.require(old.event_key(e) in assignments,'Missing actual video-confirmed finger')
        assignment=assignments[old.event_key(e)]
        old.require(assignment['finger'] in old.FINGERS and assignment['hand']==hand and int(assignment['note'])==int(e['note']),'Invalid actual fingering')
        s['assignment']=assignment
    output,joint_rows,previous=[],[],{}
    for cb,frame in sorted(data['callbacks'].items()):
        t=frame['timestamp']
        if not start<=t<end:
            continue
        pairs=data['joint_frames'][(cb,hand)]
        old.require(set(pairs)==data['raw_frames'][(cb,hand)],'Incomplete displayed joint frame')
        joints={}
        for name,pair in pairs.items():
            raw=data['raw'][(cb,hand,name)]
            check_pair(data,trial,raw,pair)
            if raw['tracking_valid'] and old.truth(pair['pose_valid']):
                a=tuple(raw['keyboard_'+axis] for axis in 'xyz')
                b=old.vec(pair,'corrected_')
                joints[name]=(a,b)
                delta=sub(b,a)
                joint_rows.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],method=trial['method'],
                    callback_index=cb,timestamp=t,joint=name,cost_mm=norm(delta)*1000,
                    **{'offset_'+axis+'_mm':delta[i]*1000 for i,axis in enumerate('xyz')}))
        all_complete=set(joints)==set(pairs)
        hand_costs=[norm(sub(b,a))*1000 for a,b in joints.values()]
        states=old.observed_states(data,t)
        for f,finger in enumerate(old.FINGERS):
            names=chain(finger)
            tip=finger+'Tip'
            contact=data['rules'].get((cb,hand,finger))
            old.require(contact is not None,'Missing finger status record')
            old.require(contact['calibration_id']==trial['calibration_id'],'Calibration ID mismatch')
            raw_tip=data['raw'].get((cb,hand,tip))
            old.require(raw_tip is not None,'Missing Raw fingertip')
            check_pair(data,trial,raw_tip,contact)
            if contact['source'] and int(contact['note'])>=0 and int(contact['event_index'])>=0:
                reference=data['event_index'].get((contact['source'],int(contact['generation']),int(contact['event_index'])))
                old.require(reference is not None and old.finite(reference['quest_observed_timestamp_sec'])<=t+1e-6,
                            'Missing or future associated MIDI event')
            phase,associated,pressing=phase_at(t,spans,finger,protocol,common)
            status_valid=old.truth(contact['coordinates_valid'])
            valid=all(n in joints for n in names) and status_valid
            active=[s for s in associated if pressing]
            match=any(int(contact['note'])==int(s['event']['note']) and contact['source']=='BLE' and
                      int(contact['generation'])==int(s['event']['connection_generation']) and
                      int(contact['channel'])==int(s['event']['channel']) and
                      (trial['method']=='Legacy' or int(contact['onset_event_index'])==int(s['event']['event_index'])) for s in active)
            solver_ok=old.truth(contact['solver_finger_valid']) if trial['method']=='FullRules' else True
            if trial['method']=='FullRules':
                match=match and rule['manualNoteMap'] and all(rule['noteToFinger'][int(s['event']['note'])]==f for s in active)
            applied=valid and trial['method']!='Raw' and old.truth(contact['enabled']) and match and solver_ok and old.finite(contact['gain'])>0
            row=dict(participant=trial['participant'],trial_id=trial['trial_id'],task=trial['task'],method=trial['method'],hand=hand,
                     callback_index=cb,snapshot_id=raw_tip['snapshot_id'],settings_revision=trial['settings_revision'],timestamp=t,
                     finger=finger,phase=phase,role='pressing' if pressing else 'nonpressing',valid=valid,
                     applied=applied,stopped=pressing and trial['method']!='Raw' and not applied,
                     state=contact['state'],reason=contact['reason'],contact_reason=contact['contact_reason'],
                     missing_rate=float(not valid),application_rate=float(applied) if pressing and trial['method']!='Raw' else None,
                     stop_rate=float(not applied) if pressing and trial['method']!='Raw' else None,
                     retrospective_approach=phase=='approach',future_target_used=False)
            if not valid:
                previous.pop(finger,None)
                output.append(row)
                continue
            positions=[[joints[n][i] for n in names] for i in (0,1)]
            a,b=joints[tip]
            delta=sub(b,a)
            row.update(tip_cost_mm=norm(delta)*1000,
                       finger_rms_cost_mm=math.sqrt(stats.mean(norm(sub(y,x))**2 for x,y in zip(*positions)))*1000,
                       hand_rms_cost_mm=math.sqrt(stats.mean(v*v for v in hand_costs)) if all_complete else None,
                       wrist_cost_mm=norm(sub(joints['Wrist'][1],joints['Wrist'][0]))*1000 if 'Wrist' in joints else None,
                       palm_cost_mm=norm(sub(joints['Palm'][1],joints['Palm'][0]))*1000 if 'Palm' in joints else None)
            row.update({'offset_'+axis+'_mm':delta[i]*1000 for i,axis in enumerate('xyz')})
            extra=(0.,0.,0.) if trial['method']=='Legacy' else old.xyz(rule['fingers'][f]['contactOffsetKeyboardMeters'])
            offset=tuple(x+y for x,y in zip(old.xyz(common['tipToContactKeyboardMeters']),extra))
            row['contact_point_model']='common offset only (Legacy)' if trial['method']=='Legacy' else 'common + finger offset'
            # Approach and quiet intervals have no future-key residual endpoint.
            if pressing:
                residuals=[]
                for s in active:
                    note=int(s['event']['note'])
                    geometry=old.recorded_surface(data,raw_tip['snapshot_id'],note)
                    errors=[old.errors(tuple(x+y for x,y in zip(p,offset)),geometry,common['belowSurfaceToleranceMeters'],common['aboveSurfaceToleranceMeters']) for p in (a,b)]
                    residuals.append(errors)
                # A physically confirmed finger can span two keys; report worst constraint residual, not a solver-selected success.
                for label,index in (('raw',0),('display',1)):
                    for axis in ('x','y','z','band','float','penetration'):
                        row[label+'_'+axis+'_mm']=max(e[index][axis] for e in residuals)*1000
                    # Preserve the signed residual of the most distant normal plane (deterministic first-event tie).
                    row[label+'_signed_y_mm']=max((e[index]['signed_y'] for e in residuals),key=abs)*1000
                    row[label+'_surface_residual_mm']=max(math.sqrt(e[index]['x']**2+e[index]['y']**2+e[index]['z']**2) for e in residuals)*1000
                    row[label+'_key_violation']=any(e[index]['band']*1000>protocol['geometry_epsilon_mm'] or
                        (math.hypot(e[index]['x'],e[index]['z'])-common['planarToleranceMeters'])*1000>protocol['geometry_epsilon_mm'] for e in residuals)
                    row[label+'_key_violation_rate']=float(row[label+'_key_violation'])
                improvement=row['raw_surface_residual_mm']-row['display_surface_residual_mm']
                row['improvement_mm']=improvement
                row['cost_per_improvement']=row['tip_cost_mm']/improvement if improvement>protocol['ratio_min_improvement_mm'] else None
            bones=[abs(norm(sub(positions[1][j+1],positions[1][j]))-norm(sub(positions[0][j+1],positions[0][j])))*1000 for j in range(3)]
            row['bone_change_mm']=max(bones)
            guide='Wrist' if f==0 else finger+'Metacarpal'
            for label,index in (('raw',0),('display',1)):
                bends,changes=[],[]
                if guide in joints:
                    for j in range(3):
                        p=positions[index]
                        parent=sub(p[j],joints[guide][index]) if j==0 else sub(p[j],p[j-1])
                        direction=sub(p[j+1],p[j])
                        bend=angle(parent,direction)
                        change=angle(sub(positions[0][j+1],positions[0][j]),direction)
                        if bend is not None and change is not None:
                            bends.append(max(0.,bend-old.xyz(rule['fingers'][f]['maximumBendDegrees'])[j]))
                            changes.append(max(0.,change-rule['fingers'][f]['maximumChangeFromRawDegrees']))
                row[label+'_bend_excess_deg']=max(bends) if len(bends)==3 else None
                row[label+'_raw_direction_excess_deg']=max(changes) if len(changes)==3 else None
                keys=data['snapshots'][int(raw_tip['snapshot_id'])]['keys']
                row[label+'_segment_penetration_mm']=penetration(positions[index],keys,rule['segmentRadiusMeters'],rule['permittedPenetrationMeters'])*1000
                for state in ('observed_released','unknown'):
                    selected=[]
                    for key in keys:
                        owners=[v for (source,channel,n),v in states.items() if n==int(key['note'])]
                        current='pressed' if any(owners) else 'observed_released' if owners else 'unknown'
                        if current==state:
                            selected.append(key)
                    row[label+'_'+state+'_penetration_mm']=penetration(positions[index],selected,rule['segmentRadiusMeters'],rule['permittedPenetrationMeters'])*1000 if selected else None
                overlaps=[]
                counterparts=0
                for other in old.FINGERS:
                    other_names=chain(other)
                    if other==finger or not all(n in joints for n in other_names):
                        continue
                    counterparts+=1
                    q=[joints[n][index] for n in other_names]
                    overlaps.extend(max(0.,2*rule['interferenceRadiusMeters']-segment_distance(u,v,w,z))*1000
                        for u,v in zip(positions[index],positions[index][1:]) for w,z in zip(q,q[1:]))
                row[label+'_interference_mm']=max(overlaps) if counterparts==4 else None
            constraint_names=('display_key_violation','bone_change_mm','display_bend_excess_deg',
                              'display_raw_direction_excess_deg','display_segment_penetration_mm','display_interference_mm')
            thresholds=(False,protocol['bone_change_tolerance_mm'],protocol['angle_tolerance_deg'],protocol['angle_tolerance_deg'],
                        protocol['geometry_epsilon_mm'],protocol['geometry_epsilon_mm'])
            violations=[row[k]>threshold for k,threshold in zip(constraint_names,thresholds) if row.get(k) is not None]
            for name,threshold in zip(constraint_names,thresholds):
                row[name+'_rate']=float(row[name]>threshold) if row.get(name) is not None else None
            expected_constraints=6 if pressing else 5
            row['constraint_violation_rate']=float(any(violations)) if any(violations) or len(violations)==expected_constraints else None
            row['constraint_coverage_rate']=len(violations)/expected_constraints
            previous[finger],motion=derivative_step(previous.get(finger),t,raw_tip['snapshot_id'],dict(raw=a,display=b,offset=delta),protocol)
            row.update(motion)
            if 'offset_step_mm' in motion:
                row['abrupt_rate']=float(motion['offset_step_mm']>protocol['abrupt_step_mm'] or motion['offset_speed_mm_s']>protocol['abrupt_offset_speed_mm_s'])
            for label,p in (('raw',a),('display',b)):
                row.update({label+'_'+axis+'_position_mm':p[i]*1000 for i,axis in enumerate('xyz')})
            output.append(row)
    old.require(output,'No trial frames')
    return output,joint_rows,spans,issues


def transitions(rows,spans,protocol):
    output=[]
    for s in spans:
        event=s['event']
        finger=s['assignment']['finger']
        selected=sorted((r for r in rows if r['finger']==finger),key=lambda r:r['timestamp'])
        held=[r for r in selected if s['start']<=r['timestamp']<s['end']]
        release=[r for r in selected if s['end']<=r['timestamp']<=s['end']+protocol['release_window_s']]
        next_on=min((other['start'] for other in spans if other['assignment']['finger']==finger and other['start']>=s['end']),default=math.inf)
        release=[r for r in release if r['timestamp']<next_on]
        first=next((r for r in held if r['valid'] and r['applied']),None)
        active=next((r for r in held if r['valid'] and r['state']=='Active' and r['applied']),None)
        pre_on=next((r for r in reversed(selected) if r['timestamp']<s['start'] and r['valid']),None)
        post_on=next((r for r in held if r['valid']),None)
        baseline=next((r['tip_cost_mm'] for r in reversed(held) if r['valid']),None)
        stable_start=None
        previous=None
        completion=None
        stable_count=0
        for r in release:
            continuous=previous and previous['snapshot_id']==r['snapshot_id'] and r['timestamp']-previous['timestamp']<=protocol['max_gap_s']
            if r['valid'] and r['tip_cost_mm']<=protocol['release_raw_tolerance_mm']:
                if stable_start is None or not continuous:
                    stable_start=r['timestamp']
                    stable_count=0
                stable_count+=1
                if stable_count>=3 and r['timestamp']-stable_start>=protocol['release_stable_s']:
                    completion=stable_start-s['end']
                    break
            else:
                stable_start=None
                stable_count=0
            previous=r if r['valid'] else None
        valid_release=[r for r in release if r['valid']]
        increments=[b['tip_cost_mm']>a['tip_cost_mm']+protocol['geometry_epsilon_mm']
                    for a,b in zip(release,release[1:]) if a['valid'] and b['valid'] and
                    a['snapshot_id']==b['snapshot_id'] and b['timestamp']-a['timestamp']<=protocol['max_gap_s']]
        if s['off'] is None:
            completion=None
        output.append(dict(source=event['source'],generation=event['connection_generation'],onset_event_index=event['event_index'],
            finger=finger,note=event['note'],onset_observed_sec=s['start'],off_observed_sec=s['end'] if s['off'] else None,
            first_applied_ms=(first['timestamp']-s['start'])*1000 if first else None,
            active_state_ms=(active['timestamp']-s['start'])*1000 if active else None,
            engage_censored=active is None,release_completion_ms=completion*1000 if completion is not None else None,
            release_censored=completion is None or s['off'] is None,
            release_censor_reason='' if completion is not None else 'missing_note_off' if s['off'] is None else
                'next_note_on' if next_on< s['end']+protocol['release_window_s'] else 'loss_or_record_end_or_not_settled',
            pre_on_cost_mm=pre_on['tip_cost_mm'] if pre_on else None,
            first_post_on_cost_mm=post_on['tip_cost_mm'] if post_on else None,
            onset_offset_jump_mm=norm(tuple(post_on['offset_'+a+'_mm']-pre_on['offset_'+a+'_mm'] for a in 'xyz'))
                if pre_on and post_on and pre_on['snapshot_id']==post_on['snapshot_id'] and
                   post_on['timestamp']-pre_on['timestamp']<=protocol['max_gap_s'] else None,
            last_pre_off_cost_mm=baseline,
            release_max_growth_mm=max((max(0.,r['tip_cost_mm']-baseline) for r in valid_release),default=None) if baseline is not None else None,
            release_first_sample_cost_mm=valid_release[0]['tip_cost_mm'] if valid_release else None,
            release_increment_rate=stats.mean(increments) if increments else None,
            timing_resolution='callback bracket; no interpolation as physical contact truth'))
    return output


def trial_summary(rows):
    groups=defaultdict(list)
    excluded={'callback_index','settings_revision','timestamp','valid','applied','stopped','future_target_used','retrospective_approach'}
    for r in rows:
        for phase,role,finger in ((r['phase'],r['role'],r['finger']),(r['phase'],r['role'],'all'),('all','all','all')):
            groups[(r['participant'],r['trial_id'],r['method'],r['task'],phase,role,finger)].append(r)
    result=[]
    for key,group in sorted(groups.items()):
        fields=sorted({k for r in group for k,v in r.items() if k not in excluded and isinstance(v,(float,int)) and not isinstance(v,bool)})
        fields+=['valid_rate']
        for field in fields:
            values=[float(r['valid']) for r in group] if field=='valid_rate' else [float(r[field]) for r in group if r.get(field) is not None]
            if not values:
                continue
            result.append(dict(zip(('participant','trial_id','method','task','phase','role','finger'),key),
                metric=field,expected_samples=len(group),valid_samples=len(values),
                mean=stats.mean(values),median=stats.median(values),p95=old.percentile(values,.95),maximum=max(values),
                sd=stats.stdev(values) if len(values)>1 else None,
                unit='fraction' if field.endswith('_rate') else 'ms' if field.endswith('_ms') else
                     'mm/s^3' if field.endswith('mm_s3') else 'mm/s^2' if field.endswith('mm_s2') else
                     'mm/s' if field.endswith('mm_s') else 'deg' if field.endswith('deg') else 'ratio' if field=='cost_per_improvement' else 'mm'))
    return result


def aggregate(trials):
    groups=defaultdict(list)
    for r in trials:
        groups[(r['method'],r['task'],r.get('hand','unspecified'),r.get('settings_signature','unspecified'),r['phase'],r['role'],r['finger'],r['metric'])].append(r)
    result=[]
    for key,group in sorted(groups.items()):
        people=defaultdict(list)
        for r in group:
            people[r['participant']].append(r['mean'])
        low,high=old.bootstrap(group,'mean')
        result.append(dict(zip(('method','task','hand','settings_signature','phase','role','finger','metric'),key),
            participants=len(people),trials=len(group),mean=stats.mean(stats.mean(v) for v in people.values()),
            trial_median=stats.median(r['mean'] for r in group),trial_p95=old.percentile([r['mean'] for r in group],.95),
            ci_low=low,ci_high=high,unit=group[0]['unit'],exploratory=True,
            ci_scope='within-person only' if len(people)==1 else 'participant/trial hierarchical',
            comparison='within-callback Raw/display pair; methods across recordings are separate trials, not a paired replay'))
    return result


def transition_trial_summary(events,trials):
    tasks={(t['participant'],t['trial_id']):t['task'] for t in trials}
    fields=('first_applied_ms','active_state_ms','release_completion_ms','release_max_growth_mm',
            'release_increment_rate','pre_on_cost_mm','first_post_on_cost_mm','onset_offset_jump_mm',
            'last_pre_off_cost_mm','release_first_sample_cost_mm')
    rows=[]
    for event in events:
        row={k:event[k] for k in fields}
        row.update(participant=event['participant'],trial_id=event['trial_id'],method=event['method'],finger=event['finger'],
                   task=tasks[event['participant'],event['trial_id']],phase='transition',role='event',valid=True,
                   engage_censored_rate=float(event['engage_censored']),release_censored_rate=float(event['release_censored']))
        rows.append(row)
    return [r for r in trial_summary(rows) if r['phase']=='transition' and r['metric']!='valid_rate']


def calibration(data,trial,records):
    output=[]
    for r in records:
        if r['session_id']!=trial['session_id'] or r['calibration_id']!=trial['calibration_id']:
            continue
        old.require(r['used_for_fit'] is False,'Held-out verification points must not be A/B/C fit points')
        old.require(r['point_id'] not in ('A','B','C') and r.get('reference_survey'),'Held-out point survey reference required')
        expected=tuple(old.finite(v) for v in r['reference_keyboard_m'])
        old.require(len(expected)==3,'Calibration point must be XYZ')
        if r['measurement_method']=='quest_tip':
            raw=data['raw'][(int(r['callback_index']),r['hand'],r['joint'])]
            old.require(raw['tracking_valid'],'Invalid calibration verification tracking')
            observed=tuple(raw['keyboard_'+a] for a in 'xyz')
            evidence='registration + Quest tracking + placement; not independent calibration accuracy'
            independent=False
        else:
            old.require(r['measurement_method']=='external' and r['registration_reference'],'External registration reference required')
            observed=tuple(old.finite(v) for v in r['observed_keyboard_m'])
            old.require(len(observed)==3 and old.finite(r['uncertainty_mm'])>=0,'Invalid external observation/uncertainty')
            evidence='external registered measurement; registration/instrument uncertainty must be reported'
            independent=True
        delta=sub(observed,expected)
        output.append(dict(session_id=r['session_id'],calibration_id=r['calibration_id'],point_id=r['point_id'],
            method=r['measurement_method'],independent_of_quest_tracking=independent,evidence=evidence,
            reference_survey=r['reference_survey'],reference_keyboard_m=json.dumps(expected),
            uncertainty_mm=r.get('uncertainty_mm'),residual_mm=norm(delta)*1000,
            **{'residual_'+axis+'_mm':delta[i]*1000 for i,axis in enumerate('xyz')}))
    old.unique(output,lambda r:(r['session_id'],r['calibration_id'],r['point_id'],r['method']))
    return output


def synchronize(data,trial,record,protocol):
    if not record:
        return None,[]
    anchors=record['anchors']
    old.require(len(anchors)>=3,'At least three independent clock anchors required')
    pairs=[(old.finite(a['video_sec']),old.finite(a['quest_sec'])) for a in anchors]
    old.require(len({a['anchor_id'] for a in anchors})==len(anchors),'Duplicate sync anchors')
    old.require(all(a.get('evidence') for a in anchors),'Clock anchor evidence reference required')
    video,quest=zip(*pairs)
    vm,qm=stats.mean(video),stats.mean(quest)
    denominator=sum((v-vm)**2 for v in video)
    old.require(denominator>0 and max(video)-min(video)>=protocol['min_anchor_span_s'],'Clock anchors have insufficient span')
    slope=sum((v-vm)*(q-qm) for v,q in pairs)/denominator
    intercept=qm-slope*vm
    old.require(slope>0,'Invalid clock map')
    residual=[abs(q-(intercept+slope*v))*1000 for v,q in pairs]
    independent=all(a['kind']=='independent_shared_event' for a in anchors)
    uncertainty=old.finite(record['video_frame_ms'])/2+max(old.finite(a['uncertainty_ms']) for a in anchors)
    old.require(record['video_frame_ms']>0 and all(a['uncertainty_ms']>=0 for a in anchors),'Invalid video/anchor uncertainty')
    if not independent:
        old.require(all(a['kind'] in ('independent_shared_event','display_event_proxy') for a in anchors),'Unknown sync anchor kind')
        uncertainty+=old.finite(record['display_latency_bound_ms'])
        old.require(record['display_latency_bound_ms']>=0,'Negative display latency uncertainty')
    mapped=[intercept+slope*v for v in video]
    old.require(min(mapped)<=trial['start_sec'] and max(mapped)>=trial['end_sec'],'Sync anchors must bracket trial; no extrapolated latency claims')
    delays=[]
    old.unique(record['contacts'],lambda a:(a['source'],int(a['generation']),int(a['onset_event_index'])))
    for annotation in record['contacts']:
        event=data['event_index'][(annotation['source'],int(annotation['generation']),int(annotation['onset_event_index']))]
        old.require(event['event_type']=='NoteOn' and int(event['velocity'])>0,'Sync contact must reference Note On')
        observed=old.finite(event['quest_observed_timestamp_sec'])
        if not trial['start_sec']<=observed<trial['end_sec']:
            continue
        contact=intercept+slope*old.finite(annotation['video_contact_sec'])
        receive=old.finite(event['quest_receive_timestamp_sec'])
        nearest=min(data['callbacks'].values(),key=lambda r:abs(r['timestamp']-observed))['timestamp']
        delays.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],source='BLE',generation=annotation['generation'],
            onset_event_index=annotation['onset_event_index'],video_contact_sec=annotation['video_contact_sec'],mapped_contact_quest_sec=contact,
            midi_receive_sec=receive,midi_observed_sec=observed,nearest_callback_sec=nearest,
            contact_to_receive_ms=(receive-contact)*1000,contact_to_observed_ms=(observed-contact)*1000,
            receive_to_observed_ms=(observed-receive)*1000,observed_to_nearest_callback_ms=(nearest-observed)*1000,
            independent_clock_alignment=independent,alignment_uncertainty_ms=uncertainty,
            interpretation='event offset; not BLE transport latency alone' if independent else 'apparent offset including display-anchor bias; not independent contact/MIDI latency'))
    summary=dict(participant=trial['participant'],trial_id=trial['trial_id'],clock_intercept_s=intercept,clock_slope=slope,
        clock_drift_ppm=(slope-1)*1e6,anchor_residual_p95_ms=old.percentile(residual,.95),alignment_uncertainty_ms=uncertainty,
        independent_clock_alignment=independent,anchors=len(anchors),contacts=len(delays))
    for field in ('contact_to_receive_ms','contact_to_observed_ms','receive_to_observed_ms','observed_to_nearest_callback_ms'):
        values=[r[field] for r in delays]
        summary[field+'_median']=stats.median(values) if values else None
        summary[field+'_sd']=stats.stdev(values) if len(values)>1 else None
        summary[field+'_p95']=old.percentile(values,.95) if values else None
    return summary,delays


def provenance(data,trial,base):
    if not trial.get('build_manifest') or not trial.get('apk_path'):
        return False,'missing_build_provenance'
    path=(base/trial['build_manifest']).resolve()
    record=json.loads(path.read_text(encoding='utf-8-sig'))
    apk=(base/trial['apk_path']).resolve()
    metadata=record['build_metadata']
    ok=(digest(apk)==record['artifact_sha256'] and apk.stat().st_size==record['artifact_bytes'] and
        metadata['git_commit_hash']==data['meta'].get('git_commit_hash') and
        metadata['git_dirty'] in ('true','false') and
        (metadata['git_dirty']=='true')==data['meta'].get('git_dirty') and
        record['unity_version']==data['meta'].get('unity_version') and
        record['build_timestamp_utc']==data['meta'].get('build_timestamp_utc') and
        record['scene']=='Assets/Research/Scenes/PianoDistributedQuest.unity' and record['target']=='Android')
    return ok,'verified_artifact_and_runtime_metadata' if ok else 'artifact_or_runtime_metadata_mismatch'


def scatter(path,rows,data_kind='measured'):
    points=[r for r in rows if r['valid'] and r.get('display_surface_residual_mm') is not None]
    # Trial means, never a cloud of autocorrelated frames masquerading as replications.
    groups=defaultdict(list)
    for r in points:
        groups[(r['participant'],r['trial_id'],r['method'])].append(r)
    pairs=[(key,stats.mean(r['display_surface_residual_mm'] for r in group),stats.mean(r['tip_cost_mm'] for r in group)) for key,group in groups.items()]
    xmax=max([1.]+[x for _,x,y in pairs])*1.1
    ymax=max([1.]+[y for _,x,y in pairs])*1.1
    colors=dict(Raw='#555555',Legacy='#2166ac',FullRules='#b2182b')
    text=['<svg xmlns="http://www.w3.org/2000/svg" width="1100" height="650"><rect width="100%" height="100%" fill="white"/><g font-family="sans-serif" font-size="14">',
          '<text x="60" y="35">Constraint residual versus correction cost (one point per trial; BLE)</text>',
          '<path d="M100 100 V500 H1000" stroke="black" fill="none"/>',
          '<text x="300" y="550">Displayed target-surface residual (mm; lower is better)</text>',
          '<text x="15" y="80">Correction cost (mm)</text>']
    for i in range(6):
        text.extend([f'<text x="{100+i*180}" y="525">{xmax*i/5:.2f}</text>',f'<text x="30" y="{505-i*80}">{ymax*i/5:.2f}</text>'])
    for key,x,y in pairs:
        text.append(f'<circle cx="{100+x/xmax*900:.2f}" cy="{500-y/ymax*400:.2f}" r="6" fill="{colors[key[2]]}"><title>{html.escape(str(key))}</title></circle>')
    text.append(f'<text x="60" y="600">Raw gray / Legacy blue / FullRules red; trials={len(pairs)}, participants={len({k[0] for k in groups})}</text>')
    if not pairs:
        text.append('<text x="350" y="300">NO MEASURED DATA; template only</text>')
    elif data_kind=='synthetic_test':
        text.append('<text x="300" y="75">SYNTHETIC TEST ONLY; NOT MEASURED RESULTS</text>')
    text.append('</g></svg>')
    path.write_text('\n'.join(text),encoding='utf-8')


def analyze(config_path,output):
    config_path,output=Path(config_path).resolve(),Path(output).resolve()
    old.require(not output.exists(),'Choose a new output directory')
    config=json.loads(config_path.read_text(encoding='utf-8-sig'))
    old.require(config['schema_version']==2,'Use pilot conditions schema version 2')
    data_kind=config['data_kind']
    old.require(data_kind in ('measured','synthetic_test'),'Declare measured or synthetic_test data_kind')
    base=config_path.parent
    lock_path=(base/config['protocol_lock']).resolve()
    lock=json.loads(lock_path.read_text(encoding='utf-8-sig'))
    protocol=lock['protocol']
    validate_protocol(protocol)
    old.require(hashlib.sha256(canonical(protocol)).hexdigest()==lock['protocol_sha256']==config['protocol_sha256'],'Preregistered thresholds changed or lock digest mismatched')
    locked_at=utc(lock['locked_at_utc'])
    old.unique(config['trials'],lambda t:(t['participant'],t['trial_id']))
    datasets={}
    session_paths={}
    evidence_hashes={str(config_path):digest(config_path),str(lock_path):digest(lock_path)}
    evidence={}
    for name in ('calibration_file','synchronization_file'):
        path=(base/config[name]).resolve()
        evidence_hashes[str(path)]=digest(path)
        evidence[name]=json.loads(path.read_text(encoding='utf-8-sig'))
    for trial in config['trials']:
        path=(base/trial['session_dir']).resolve()
        old.require(path!=output and path not in output.parents and output not in path.parents,'Output overlaps source recording')
        if path not in datasets:
            datasets[path]=old.load_session(path)
            require_ble(datasets[path])
            sid=datasets[path]['meta']['session_id']
            old.require(sid not in session_paths,'Same session copied into multiple paths; not independent trials')
            session_paths[sid]=path
        data=datasets[path]
        old.require(data['meta']['session_id']==trial['session_id'],'Session ID mismatch')
        old.require(locked_at<utc(data['meta']['start_utc']),'Protocol was not locked before recording')
        evidence_hashes.update(data['source_hashes'])
        for name in ('build_manifest','apk_path'):
            if trial.get(name):
                path=(base/trial[name]).resolve()
                evidence_hashes[str(path)]=digest(path)
    for path in datasets:
        intervals=sorted((old.finite(t['start_sec']),old.finite(t['end_sec'])) for t in config['trials'] if (base/t['session_dir']).resolve()==path)
        old.require(all(b<=c for (a,b),(c,d) in zip(intervals,intervals[1:])),'Overlapping trial intervals')
    frames,joints,events,calibrations,syncs,delays,quality=[],[],[],[],[],[],[]
    for trial in config['trials']:
        data=datasets[(base/trial['session_dir']).resolve()]
        rows,joint,spans,issues=metric_rows(data,trial,protocol)
        frames.extend(rows)
        joints.extend(joint)
        events.extend(dict(r,participant=trial['participant'],trial_id=trial['trial_id'],method=trial['method']) for r in transitions(rows,spans,protocol))
        cal=calibration(data,trial,evidence['calibration_file']['points'])
        calibrations.extend(dict(r,participant=trial['participant'],trial_id=trial['trial_id']) for r in cal)
        sync_record=next((r for r in evidence['synchronization_file']['sessions'] if r['session_id']==trial['session_id']),None)
        sync,delay=synchronize(data,trial,sync_record,protocol)
        if sync:
            syncs.append(sync)
        delays.extend(delay)
        valid_rate=stats.mean(r['valid'] for r in rows)
        callback_times=sorted({r['timestamp'] for r in rows})
        observed_gaps=[b-a for a,b in zip([trial['start_sec']]+callback_times,callback_times+[trial['end_sec']])]
        maximum_gap=max(observed_gaps)
        pressed=[r for r in rows if r['role']=='pressing']
        stopped=stats.mean(r['stopped'] for r in pressed) if pressed else 1.
        application=stats.mean(r['applied'] for r in pressed) if pressed else 0.
        build_ok,build_reason=provenance(data,trial,base)
        setting=data['settings'][int(trial['settings_revision'])]
        flags=trial['method']!='FullRules' or all(setting['rule'][k] for k in ('contact','joints','nonpenetration','interference'))
        # Do not count repeated measurements/labels at the same physical coordinate as distinct validation points.
        cal_groups=defaultdict(list)
        for r in cal:
            cal_groups[r['method']].append(r)
        cal_ok=any(len({r['reference_keyboard_m'] for r in group})>=protocol['min_calibration_points'] and
                   old.percentile([r['residual_mm'] for r in group],.95)<=protocol['max_calibration_p95_mm'] for group in cal_groups.values())
        expected_keys={old.event_key(s['event']) for s in spans}
        measured_keys={(r['source'],int(r['generation']),int(r['onset_event_index'])) for r in delay}
        sync_ok=bool(sync and sync['anchor_residual_p95_ms']<=protocol['max_sync_residual_p95_ms'] and
                     sync['alignment_uncertainty_ms']<=protocol['max_sync_uncertainty_ms'] and
                     abs(sync['clock_drift_ppm'])<=protocol['max_clock_drift_ppm'] and expected_keys<=measured_keys)
        checks=dict(required_logs=True,ble_only=True,conditions_valid=True,thresholds_locked_before_recording=True,
            midi_on_off_paired=not issues,valid_frame_rate=valid_rate>=protocol['min_valid_rate'],
            stopped_rate=trial['method']=='Raw' or stopped<=protocol['max_stopped_rate'],
            actual_application=trial['method']!='FullRules' or application>=protocol['min_application_rate'],
            fullrules_flags=flags,heldout_calibration=cal_ok,synchronization=sync_ok,build_traceability=build_ok,
            raw_off_identity=trial['method']!='Raw' or all(r.get('tip_cost_mm',0)<=protocol['geometry_epsilon_mm'] for r in rows if r['valid']))
        checks['callback_gaps']=maximum_gap<=protocol['max_gap_s']
        quality.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],method=trial['method'],task=trial['task'],
            expected_finger_frames=len(rows),valid_finger_frames=sum(r['valid'] for r in rows),valid_rate=valid_rate,
            stop_rate=stopped,application_rate=application,display_changed_frames=sum(r.get('tip_cost_mm',0)>protocol['geometry_epsilon_mm'] for r in rows),
            max_callback_gap_ms=maximum_gap*1000,gaps_over_limit=sum(v>protocol['max_gap_s'] for v in observed_gaps),
            midi_issues=';'.join(issues),build_reason=build_reason,passed=all(checks.values()),**checks))
    trial_stats=trial_summary(frames)
    trial_stats.extend(transition_trial_summary(events,config['trials']))
    trial_lookup={(t['participant'],t['trial_id']):t for t in config['trials']}
    for row in trial_stats:
        trial=trial_lookup[row['participant'],row['trial_id']]
        settings=datasets[(base/trial['session_dir']).resolve()]['settings'][int(trial['settings_revision'])]
        row.update(hand=trial['hand'],settings_revision=trial['settings_revision'],calibration_id=trial['calibration_id'],session_id=trial['session_id'],
                   settings_signature=hashlib.sha256(canonical(dict(common=settings['common'],rule=settings['rule']))).hexdigest())
    population=aggregate(trial_stats)
    output.mkdir(parents=True)
    for filename,rows,fields in (('phase_frames.csv',frames,['participant','trial_id','method','phase','role','finger','valid']),
        ('joint_costs.csv',joints,['participant','trial_id','joint','cost_mm']),
        ('trial_metrics.csv',trial_stats,['participant','trial_id','method','metric','mean','median','p95','maximum']),
        ('method_summary.csv',population,['method','metric','participants','trials','mean','ci_low','ci_high']),
        ('transitions.csv',events,['participant','trial_id','onset_event_index','engage_censored','release_censored']),
        ('calibration_residuals.csv',calibrations,['point_id','method','residual_mm']),
        ('synchronization_summary.csv',syncs,['trial_id','clock_intercept_s','clock_slope']),
        ('contact_midi_offsets.csv',delays,['trial_id','contact_to_receive_ms','contact_to_observed_ms']),
        ('pilot_quality.csv',quality,['participant','trial_id','passed'])):
        old.table(output/filename,rows,fields)
    scatter(output/'residual_vs_cost.svg',frames,data_kind)
    for category,metrics in (('A_constraints',('display_surface_residual_mm','display_bend_excess_deg','bone_change_mm','constraint_violation_rate')),
                             ('B_cost',('tip_cost_mm','finger_rms_cost_mm','hand_rms_cost_mm')),
                             ('C_continuity',('offset_step_mm','offset_speed_mm_s','offset_acceleration_mm_s2','offset_jerk_mm_s3','abrupt_rate',
                                              'first_applied_ms','release_completion_ms','release_max_growth_mm','release_increment_rate')),
                             ('D_nonpressing',('tip_cost_mm','display_observed_released_penetration_mm','display_interference_mm'))):
        for metric in metrics:
            selected=[r for r in trial_stats if r['metric']==metric and r['finger']=='all' and r['phase']!='all' and (category!='D_nonpressing' or r['role']=='nonpressing')]
            old.svg(output/(category+'_'+metric+'.svg'),category+' / '+metric,
                [r['participant']+'/'+r['trial_id']+'/'+r['method']+'/'+r['phase'] for r in selected],
                [('trial mean',[r['mean'] for r in selected])],unit=selected[0]['unit'] if selected else 'see metric name',
                subtitle=(('SYNTHETIC TEST ONLY; NOT MEASURED RESULTS; ' if data_kind=='synthetic_test' else '')+
                    f'BLE; trials={len({(r["participant"],r["trial_id"]) for r in selected})}; participants={len({r["participant"] for r in selected})}; preregistered conditions_snapshot.json') if selected else 'NO MEASURED DATA; template only')
    gate=dict(required_trials=protocol['required_trials'],observed_trials=len(quality),
              all_trials_passed=bool(quality) and all(r['passed'] for r in quality),
              has_fullrules=any(r['method']=='FullRules' for r in quality),
              has_five_tasks=set(r['task'] for r in quality)==set(old.TASKS),
              measured=bool(frames) and data_kind=='measured',data_kind=data_kind,physical_device_verified=False)
    gate['data_quality_passed']=gate['observed_trials']==gate['required_trials'] and gate['all_trials_passed'] and gate['has_fullrules'] and gate['has_five_tasks']
    gate['ready_for_main_experiment']=gate['measured'] and gate['data_quality_passed']
    dump(output/'pilot_gate.json',gate)
    dump(output/'conditions_snapshot.json',config)
    dump(output/'protocol_snapshot.json',lock)
    manifest=dict(schema_version=2,source_sha256=evidence_hashes,protocol_sha256=lock['protocol_sha256'],
        tool_sha256={n:digest(Path(__file__).with_name(n)) for n in ('evaluate_pilot.py','analyze_rule_effect.py','analyze_raw_session.py')},
        python=sys.version,measured=gate['measured'],data_kind=data_kind,frames=len(frames),
        limits='No external hand ground truth; no causal future-key scoring; clock-proxy bias explicitly labelled; no solver replay claimed',
        statistics='trial summaries then equal participant weight; hierarchical percentile bootstrap seed 20261027, 2000 replicates; one subject is within-person exploratory',
        units='input metres / output mm; time derivatives on nonuniform midpoint timestamps; no differentiation across loss, snapshot or sampling bound violations',
        display='quest_rule_pose_joints actual post-processor pose; never unconditional Raw substitution; callback output, not photon time')
    old.require(all(digest(p)==h for p,h in evidence_hashes.items()),'Input changed during analysis')
    dump(output/'analysis_manifest.json',manifest)
    return gate


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    subparsers=parser.add_subparsers(dest='command',required=True)
    lock=subparsers.add_parser('lock-protocol')
    lock.add_argument('protocol')
    lock.add_argument('--output',required=True)
    run=subparsers.add_parser('analyze')
    run.add_argument('conditions')
    run.add_argument('--output',required=True)
    init=subparsers.add_parser('init-study')
    init.add_argument('--output',required=True)
    args=parser.parse_args()
    try:
        if args.command=='init-study':
            init_study(args.output)
            print('Study initialized with locked thresholds and empty evidence templates; no measurements.')
        elif args.command=='lock-protocol':
            lock_protocol(args.protocol,args.output)
            print('Protocol locked. Copy protocol_sha256 into the conditions before recording.')
        else:
            print(json.dumps(analyze(args.conditions,args.output),indent=2))
    except (ValueError,KeyError,TypeError,OSError) as e:
        parser.exit(2,'Pilot evaluation rejected: '+str(e)+'\n')


if __name__=='__main__':
    main()
