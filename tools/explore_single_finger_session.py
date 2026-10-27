"""Exploratory single-finger analysis; never substitutes for video-verified trials.

Read one immutable Quest session. All times are Quest observation seconds; all
primary frames are paired by callback and lie in [Note On, Note Off). The
operator-supplied finger is explicitly self-reported, not video verified.
"""
import argparse
import csv
import hashlib
import html
import json
import math
import statistics as st
from collections import Counter
from pathlib import Path

import analyze_rule_effect as base
import evaluate_pilot as pilot

EPS_MM = .001  # 1 micrometre; classifications only, not the mean differences.
MOVEMENT_MM = .001
NAMES = ('C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B')


def note_name(note):
    return NAMES[note % 12] + str(note // 12 - 1)


def paired_spans(events, source, generation, first, last):
    """Require observed explicit off events; never infer a missing release."""
    selected = [e for e in events if e['source'] == source
                and int(e['connection_generation']) == generation
                and first <= int(e['event_index']) <= last
                and e['event_type'] == 'NoteOn' and int(e['velocity']) > 0
                and not base.truth(e['synthetic'])]
    base.require(selected and int(selected[0]['event_index']) == first
                 and int(selected[-1]['event_index']) == last, 'Onset endpoints missing')
    spans = []
    for i, onset in enumerate(selected):
        start = base.finite(onset['quest_observed_timestamp_sec'])
        boundary = (base.finite(selected[i+1]['quest_observed_timestamp_sec'])
                    if i+1 < len(selected) else float('inf'))
        following = [e for e in events if e['source'] == source
                     and int(e['connection_generation']) == generation
                     and int(e['channel']) == int(onset['channel'])
                     and int(e['note']) == int(onset['note'])
                     and int(e['event_index']) > int(onset['event_index'])
                     and start <= base.finite(e['quest_observed_timestamp_sec']) < boundary
                     and not base.truth(e['synthetic'])
                     and (e['event_type'] == 'NoteOff' or
                          e['event_type'] == 'NoteOn' and int(e['velocity']) == 0)]
        base.require(len(following) == 1, 'Missing or ambiguous explicit Note Off')
        off = following[0]
        end = base.finite(off['quest_observed_timestamp_sec'])
        base.require(start < end <= boundary, 'Overlapping or invalid single-finger notes')
        spans.append(dict(index=i+1, note=int(onset['note']), name=note_name(int(onset['note'])),
                          source=source, generation=generation, channel=int(onset['channel']),
                          onset_event_index=int(onset['event_index']), off_event_index=int(off['event_index']),
                          on=start, off=end, receive_on=base.finite(onset['quest_receive_timestamp_sec']),
                          receive_off=base.finite(off['quest_receive_timestamp_sec'])))
    return spans


def classify(delta, eps=EPS_MM):
    return 'improved' if delta > eps else 'worsened' if delta < -eps else 'unchanged'


def ratio(raw, corrected):
    return None if raw == 0 else (raw-corrected)/raw*100


def avg(values):
    return st.mean(values) if values else None


def percentile(values, q):
    return base.percentile(values, q) if values else None


def extract_frames(data, spans, hand, finger, revision):
    start, end = spans[0]['on'], spans[-1]['off']
    settings = data['settings'][revision]
    common, rules = settings['common'], settings['rule']
    base.require(settings['configuration_valid'] and common['enabled'] and not rules['manualNoteMap'],
                 'Not a valid enabled single-finger setting')
    base.require((hand == 'Left') == common['leftHand'] and base.FINGERS[common['finger']] == finger,
                 'Self-reported finger does not match configured single finger')
    base.require(all(rules[x] for x in ('contact','joints','nonpenetration','interference')),
                 'Expected all FullRules constraints enabled')
    trial = dict(settings_revision=revision)
    offset = tuple(a+b for a,b in zip(base.xyz(common['tipToContactKeyboardMeters']),
                                      base.xyz(rules['fingers'][base.FINGERS.index(finger)]['contactOffsetKeyboardMeters'])))
    rows, frame_side, prev = [], [], None
    interval_end = min(max(x['timestamp'] for x in data['callbacks'].values()), end+.3)
    for cb, frame in sorted(data['callbacks'].items()):
        t = frame['timestamp']
        if not start <= t <= interval_end:
            continue
        raw = data['raw'].get((cb, hand, finger+'Tip'))
        pair = data['joints'].get((cb, hand, finger+'Tip'))
        contact = data['rules'].get((cb, hand, finger))
        base.require(raw is not None and pair is not None and contact is not None, 'Missing paired tip/contact row')
        base.check_pair(raw,pair,trial)
        base.check_pair(raw,contact,trial)
        valid = raw['tracking_valid'] and base.truth(pair['pose_valid']) and base.truth(contact['coordinates_valid'])
        span = next((s for s in spans if s['on'] <= t < s['off']), None)
        row = dict(callback_index=cb, timestamp_sec=t, note=span['note'] if span else None,
                   strike=span['index'] if span else None, state=contact['state'], reason=contact['reason'],
                   contact_reason=contact['contact_reason'], tracking_valid=valid,
                   solver_valid=base.truth(contact['solver_finger_valid']), enabled=base.truth(contact['enabled']),
                   target_capped=base.truth(contact['target_capped']), solver_limited=base.truth(contact['solver_limited']),
                   processing_ms=base.finite(contact['processing_ms']) if contact['processing_ms'] else None)
        matched = bool(span and int(contact['note']) == span['note'] and contact['source'] == span['source']
                       and int(contact['generation']) == span['generation'] and int(contact['channel']) == span['channel']
                       and int(contact['onset_event_index']) == span['onset_event_index'])
        row['assignment_matched'] = matched
        row['applicable'] = bool(valid and matched and row['enabled'] and row['solver_valid']
                                 and contact['state'] in ('Engaging','Active') and base.finite(contact['gain']) > 0)
        row['not_applied_reason'] = ('' if row['applicable'] else
            'tracking_or_coordinates_invalid' if not valid else 'disabled' if not row['enabled'] else
            'solver_invalid_or_not_run' if not row['solver_valid'] else 'midi_assignment_not_matched' if not matched else
            'state_'+contact['state'] if contact['state'] not in ('Engaging','Active') else 'zero_gain')
        if valid:
            a = tuple(raw['keyboard_'+k] for k in 'xyz')
            b = base.vec(pair,'corrected_')
            for label,p in (('raw',a),('corrected',b)):
                base.require(max(abs(p[i]-base.finite(contact[label+'_'+k])) for i,k in enumerate('xyz')) < 2e-6,
                             'Contact and joint coordinates disagree')
                for i,k in enumerate('xyz'):
                    row[label+'_pos_'+k+'_mm'] = p[i]*1000
                if span:
                    geom=base.recorded_surface(data,raw['snapshot_id'],span['note'])
                    metrics=base.errors(tuple(p[i]+offset[i] for i in range(3)),geom,
                                        common['belowSurfaceToleranceMeters'],common['aboveSurfaceToleranceMeters'])
                    row.update({label+'_'+k+'_mm':v*1000 for k,v in metrics.items()})
            row['displacement_mm']=base.distance(a,b)*1000
            row['moved']=row['displacement_mm']>MOVEMENT_MM
            if span:
                for axis in ('x','y','z','signed_y','float','penetration','band'):
                    if axis != 'signed_y':
                        row['improvement_'+axis+'_mm']=row['raw_'+axis+'_mm']-row['corrected_'+axis+'_mm']
                for axis in 'xyz':
                    row['classification_'+axis]=classify(row['improvement_'+axis+'_mm'])
                for phase in ('before','after'):
                    for metric in ('planar','normal'):
                        key=metric+'_'+phase+'_m'
                        row['logger_'+metric+'_'+phase+'_mm']=base.finite(contact[key])*1000 if contact.get(key) else None
            # Solver diagnostics are distinct from the independently reconstructed target-key residuals.
            for metric in ('penetration','interference'):
                for state in ('before','after'):
                    value=contact[metric+'_'+state+'_m']
                    row['logger_'+metric+'_'+state+'_mm']=base.finite(value)*1000 if value else None
            row['logger_angular_violation_deg']=base.finite(contact['angular_violation_deg']) if contact['angular_violation_deg'] else None
            row['logger_target_residual_mm']=base.finite(contact['tip_target_residual_m'])*1000 if contact['tip_target_residual_m'] else None
        rows.append(row)

        # Independently calculate bone lengths, bend envelopes, and movement for all fingers.
        joints={}
        for name,j in data['joint_frames'][(cb,hand)].items():
            r=data['raw'].get((cb,hand,name))
            base.require(r is not None, 'Joint missing Raw counterpart')
            base.check_pair(r,j,trial)
            if r['tracking_valid'] and base.truth(j['pose_valid']):
                joints[name]=(tuple(r['keyboard_'+k] for k in 'xyz'),base.vec(j,'corrected_'))
        sd=dict(callback_index=cb,timestamp_sec=t,strike=row['strike'],state=row['state'],reason=row['reason'])
        bone=[]; angles_raw=[]; angles_corrected=[]; moves=[]; other=[]
        for f in base.FINGERS:
            chain=pilot.chain(f); guide='Wrist' if f=='Thumb' else f+'Metacarpal'
            if all(n in joints for n in chain):
                moves.extend(base.distance(joints[n][0],joints[n][1])*1000 for n in chain[1:])
                if f != finger or span is None:
                    other.append(base.distance(joints[f+'Tip'][0],joints[f+'Tip'][1])*1000)
                for x,y in zip(chain,chain[1:]):
                    bone.append(abs(base.distance(joints[x][0],joints[y][0])-base.distance(joints[x][1],joints[y][1]))*1000)
                seq=[guide]+chain
                for j in range(3):
                    if all(n in joints for n in seq[j:j+3]):
                        names=seq[j:j+3];limit=base.xyz(rules['fingers'][base.FINGERS.index(f)]['maximumBendDegrees'])[j]
                        for which,out in ((0,angles_raw),(1,angles_corrected)):
                            p,q,r=[joints[n][which] for n in names]
                            angle=pilot.angle(pilot.sub(q,p),pilot.sub(r,q))
                            if angle is not None:out.append(max(0.,angle-limit))
        sd.update(max_bone_change_mm=max(bone) if bone else None,
                  raw_max_bend_excess_deg=max(angles_raw) if angles_raw else None,
                  corrected_max_bend_excess_deg=max(angles_corrected) if angles_corrected else None,
                  max_joint_displacement_mm=max(moves) if moves else None,
                  hand_rms_displacement_mm=math.sqrt(st.mean(x*x for x in moves)) if moves else None,
                  nonpress_tip_mean_displacement_mm=avg(other),nonpress_tip_max_displacement_mm=max(other) if other else None,
                  raw_index_segment_penetration_proxy_mm=row.get('logger_penetration_before_mm'),
                  corrected_index_segment_penetration_proxy_mm=row.get('logger_penetration_after_mm'),
                  raw_interference_proxy_mm=row.get('logger_interference_before_mm'),
                  corrected_interference_proxy_mm=row.get('logger_interference_after_mm'))
        for metric in ('penetration','interference'):
            for phase in ('before','after'):
                vals=[base.finite(finger_row[metric+'_'+phase+'_m'])*1000 for f in base.FINGERS
                      if (finger_row:=data['rules'].get((cb,hand,f))) and finger_row.get(metric+'_'+phase+'_m')]
                sd['logger_all_finger_'+metric+'_'+phase+'_max_mm']=max(vals) if vals else None
        if valid:
            positions={'raw':tuple(row['raw_pos_'+k+'_mm']/1000 for k in 'xyz'),
                       'corrected':tuple(row['corrected_pos_'+k+'_mm']/1000 for k in 'xyz'),
                       'offset':tuple((row['corrected_pos_'+k+'_mm']-row['raw_pos_'+k+'_mm'])/1000 for k in 'xyz')}
            prev,derivatives=pilot.derivative_step(prev,t,int(raw['snapshot_id']),positions,{'min_dt_s':.001,'max_gap_s':.1})
            sd.update(derivatives)
        else:prev=None
        frame_side.append(sd)
    base.require(rows and all(r['enabled'] for r in rows if r['timestamp_sec'] < end), 'Disabled or missing session frames')
    return rows,frame_side


def summarize(rows, spans):
    trials=[]
    for span in spans:
        group=[r for r in rows if r['strike']==span['index']]
        valid=[r for r in group if r['tracking_valid']]
        base.require(group, 'No callback in a note interval')
        tr=dict(strike=span['index'],note=span['note'],note_name=span['name'],on_sec=span['on'],off_sec=span['off'],
                receive_on_sec=span['receive_on'],receive_off_sec=span['receive_off'],onset_event_index=span['onset_event_index'],
                frames=len(group),valid_frames=len(valid),invalid_frames=len(group)-len(valid),
                applicable_frames=sum(r['applicable'] for r in group),moved_frames=sum(r.get('moved',False) for r in group),
                moved_pct=100*sum(r.get('moved',False) for r in group)/len(group),
                mean_displacement_mm=avg([r['displacement_mm'] for r in valid]),
                max_displacement_mm=max((r['displacement_mm'] for r in valid),default=None),
                constraints_remaining_frames=sum(r['reason']=='constraints_remaining' for r in group),
                displacement_limited_frames=sum(r['reason']=='displacement_limited' for r in group),
                idle_constraints_frames=sum(r['reason']=='idle_constraints' for r in group),
                target_capped_frames=sum(r['target_capped'] for r in group),solver_limited_frames=sum(r['solver_limited'] for r in group),
                not_applied_reasons='; '.join(f'{k}:{v}' for k,v in sorted(Counter(r['not_applied_reason'] for r in group if r['not_applied_reason']).items())))
        for axis in ('x','y','z','signed_y','float','penetration','band'):
            a=avg([r['raw_'+axis+'_mm'] for r in valid]);b=avg([r['corrected_'+axis+'_mm'] for r in valid])
            tr['raw_'+axis+'_mean_mm']=a;tr['corrected_'+axis+'_mean_mm']=b
            if axis!='signed_y':
                tr['improvement_'+axis+'_mm']=a-b if a is not None else None
                tr['improvement_'+axis+'_pct']=ratio(a,b) if a is not None else None
        for axis in 'xyz':
            counts=Counter(r['classification_'+axis] for r in valid)
            for k in ('improved','worsened','unchanged'):
                tr[axis+'_'+k+'_pct']=100*counts[k]/len(valid) if valid else None
        trials.append(tr)
    valid=[r for r in rows if r['strike'] and r['tracking_valid']]
    summary=[]
    for axis in ('x','y','z','signed_y','float','penetration','band'):
        a=avg([r['raw_'+axis+'_mm'] for r in valid]);b=avg([r['corrected_'+axis+'_mm'] for r in valid])
        per=[x['improvement_'+axis+'_mm'] for x in trials if axis!='signed_y' and x['improvement_'+axis+'_mm'] is not None]
        rates=[x['improvement_'+axis+'_pct'] for x in trials if axis!='signed_y' and x['improvement_'+axis+'_pct'] is not None]
        counts=Counter(r['classification_'+axis] for r in valid) if axis in 'xyz' else Counter()
        summary.append(dict(axis=axis,valid_frames=len(valid),strikes=len(trials),raw_mean_mm=a,corrected_mean_mm=b,
            improvement_mm=(a-b if axis!='signed_y' else None),improvement_pct=(ratio(a,b) if axis!='signed_y' else None),
            improved_pct=100*counts['improved']/len(valid) if axis in 'xyz' else None,
            worsened_pct=100*counts['worsened']/len(valid) if axis in 'xyz' else None,
            unchanged_pct=100*counts['unchanged']/len(valid) if axis in 'xyz' else None,
            strike_mean_improvement_mm=avg(per),strike_median_improvement_mm=st.median(per) if per else None,
            strike_sd_improvement_mm=st.stdev(per) if len(per)>1 else None,
            strike_rate_defined_n=len(rates),strike_rate_mean_pct=avg(rates),
            strike_rate_median_pct=st.median(rates) if rates else None,
            strike_rate_sd_pct=st.stdev(rates) if len(rates)>1 else None,
            strike_improved=sum(x>EPS_MM for x in per),strike_worsened=sum(x < -EPS_MM for x in per),
            strike_unchanged=sum(abs(x)<=EPS_MM for x in per)))
    return trials,summary


def release_summary(rows, sides, spans):
    result=[]
    by_callback={r['callback_index']:r for r in sides}
    for i,span in enumerate(spans):
        next_on=spans[i+1]['on'] if i+1<len(spans) else float('inf')
        end=min(span['off']+.3,next_on)
        after=[r for r in rows if span['off']<=r['timestamp_sec']<end and r['tracking_valid']]
        onset=[r for r in rows if span['on']<=r['timestamp_sec']<min(span['on']+.1,span['off']) and r['tracking_valid']]
        inactive=next((r for r in after if r['state']=='Inactive'),None)
        near_raw=next((r for r in after if r['displacement_mm']<=.01),None)
        steps=[by_callback[r['callback_index']].get('offset_step_mm') for r in onset]
        steps=[x for x in steps if x is not None]
        result.append(dict(strike=span['index'],note=span['note'],on_sec=span['on'],off_sec=span['off'],
            observed_release_window_s=end-span['off'],release_window_frames=len(after),
            releasing_frames=sum(r['state']=='Releasing' for r in after),
            constraints_remaining_frames=sum(r['reason']=='constraints_remaining' for r in after),
            displacement_limited_frames=sum(r['reason']=='displacement_limited' for r in after),
            idle_constraints_frames=sum(r['reason']=='idle_constraints' for r in after),
            inactive_first_ms=(inactive['timestamp_sec']-span['off'])*1000 if inactive else None,
            within_0_01mm_first_ms=(near_raw['timestamp_sec']-span['off'])*1000 if near_raw else None,
            next_on_truncated=next_on < span['off']+.3,
            first_release_displacement_mm=after[0]['displacement_mm'] if after else None,
            last_release_displacement_mm=after[-1]['displacement_mm'] if after else None,
            onset_first_100ms_max_offset_step_mm=max(steps) if steps else None))
    return result


def finger_summary(data, rows, hand, selected):
    result=[]
    for finger in base.FINGERS:
        for period in ('key_down','between_notes'):
            frames=[r for r in rows if bool(r['strike'])==(period=='key_down')]
            vals=[]
            for frame in frames:
                if period=='key_down' and finger==selected:continue
                contact=data['rules'].get((frame['callback_index'],hand,finger))
                if contact and base.truth(contact['coordinates_valid']) and contact['delta_m']:
                    vals.append(base.finite(contact['delta_m'])*1000)
            if vals:
                result.append(dict(finger=finger,period=period,valid_frames=len(vals),
                    moved_frames=sum(v>MOVEMENT_MM for v in vals),mean_displacement_mm=st.mean(vals),
                    median_displacement_mm=st.median(vals),p95_displacement_mm=percentile(vals,.95),max_displacement_mm=max(vals)))
    return result


def write_csv(path,rows):
    base.require(rows,'Cannot write empty result: '+str(path))
    fields=list(dict.fromkeys(k for r in rows for k in r))
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields,extrasaction='raise');w.writeheader();w.writerows(rows)


def line_svg(path,title,rows,panels,start,end,subtitle):
    """Time-series companion to the project's SVG plotting helper; never bridge gaps."""
    width=1200;ph=180;height=130+ph*len(panels)
    parts=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
           '<rect width="100%" height="100%" fill="white"/><g font-family="sans-serif" fill="#222">',
           f'<text x="45" y="32" font-size="20">{html.escape(title)}</text>',
           f'<text x="45" y="57" font-size="13">{html.escape(subtitle)}</text>']
    colors=['#2166ac','#b2182b','#238b45','#8c510a']
    for n,(label,series) in enumerate(panels):
        vals=[r[k] for r in rows for k,_ in series if r.get(k) is not None]
        if not vals:continue
        low,high=min(vals),max(vals)
        if high-low<1e-9:high=low+1
        pad=(high-low)*.08;low-=pad;high+=pad
        top=80+n*ph;bottom=top+120
        xx=lambda t:100+(t-start)/(end-start)*1030
        yy=lambda v:bottom-(v-low)/(high-low)*120
        parts += [f'<text x="8" y="{top+14}" font-size="12">{html.escape(label)}</text>',
                  f'<text x="8" y="{top+31}" font-size="11">{high:.3g}</text>',
                  f'<text x="8" y="{bottom}" font-size="11">{low:.3g}</text>',
                  f'<path d="M100 {bottom} H1130 M100 {top} V{bottom}" stroke="#999" fill="none"/>']
        for j,(key,name) in enumerate(series):
            prev=None
            for r in rows:
                v=r.get(key);t=r['timestamp_sec']
                if v is not None and prev is not None and 0<t-prev[0]<=.1:
                    parts.append(f'<path d="M{xx(prev[0]):.2f} {yy(prev[1]):.2f} L{xx(t):.2f} {yy(v):.2f}" stroke="{colors[j]}" stroke-width="1.3" fill="none"/>')
                prev=(t,v) if v is not None else None
            parts.append(f'<text x="{110+j*220}" y="{bottom+20}" font-size="12" fill="{colors[j]}">{html.escape(name)}</text>')
    parts.append(f'<text x="100" y="{height-10}" font-size="12">Quest observation seconds: {start:.6f} to {end:.6f}</text></g></svg>')
    path.write_text('\n'.join(parts),encoding='utf-8')


def figures(out,rows,sides,trials,summary,spans):
    start,end=spans[0]['on'],spans[-1]['off']; valid=[r for r in rows if r['strike'] and r['tracking_valid']]
    caption=f'Exploratory; one session, 16 key-downs; observed {start:.3f}-{end:.3f} s; valid pairs n={len(valid)}; frames not independent'
    xyz=[next(r for r in summary if r['axis']==a) for a in 'xyz']
    base.svg(out/'01_xyz_mean.svg','Raw / FullRules mean distance to effective key surface',list('XYZ'),
             [('Raw',[x['raw_mean_mm'] for x in xyz]),('FullRules',[x['corrected_mean_mm'] for x in xyz])],subtitle=caption)
    base.svg(out/'02_xyz_improvement.svg','Mean improvement by axis (Raw - FullRules)',list('XYZ'),
             [('Improvement',[x['improvement_mm'] for x in xyz])],subtitle=caption)
    base.svg(out/'03_strike_improvement.svg','Improvement per strike', [str(x['strike'])+'/'+x['note_name'] for x in trials],
             [(a.upper(),[x['improvement_'+a+'_mm'] for x in trials]) for a in 'xyz'],subtitle=caption)
    track=[r for r in rows if start<=r['timestamp_sec']<end]
    time_caption=f'Exploratory; one session, observed {start:.3f}-{end:.3f} s; valid timeline n={sum(r["tracking_valid"] for r in track)}; key-down pairs n={len(valid)}'
    line_svg(out/'04_tip_trajectory.svg','Index tip coordinate over time',track,
             [(a.upper()+' (mm)',[('raw_pos_'+a+'_mm','Raw'),('corrected_pos_'+a+'_mm','FullRules')]) for a in 'xyz'],start,end,time_caption)
    states={'Inactive':0,'Engaging':1,'Active':2,'Releasing':3}
    timeline=[dict(r,state_number=states.get(r['state'])) for r in track]
    line_svg(out/'05_midi_and_correction_state.svg','MIDI note and correction phase',timeline,
             [('MIDI note',[('note','key-down note')]),('State code',[('state_number','0 idle, 1 engage, 2 active, 3 release')])],start,end,
             time_caption+'; gaps in note line indicate Note Off; complete On/Off in strike CSV')
    displacement=[r['displacement_mm'] for r in valid]
    edges=[0,.001,1,3,5,10,15,20]
    counts=[sum(edges[i]<=v<edges[i+1] for v in displacement) for i in range(len(edges)-1)]
    base.svg(out/'06_displacement_distribution.svg','Index tip displacement histogram',
             [f'{edges[i]}-{edges[i+1]}' for i in range(len(counts))],[('frames',counts)],unit='frame count; bins in mm',subtitle=caption)
    categories=('constraints_remaining','displacement_limited','idle_constraints')
    base.svg(out/'07_solver_reasons.svg','Solver reason counts: key-down and between notes',list(categories),
             [('Key-down',[sum(r['reason']==reason and bool(r['strike']) for r in track) for reason in categories]),
              ('Between notes',[sum(r['reason']==reason and not r['strike'] for r in track) for reason in categories])],
             unit='frame count',subtitle=time_caption+'; codes are observations, not established causes')
    base.svg(out/'07b_solver_reasons_by_strike.svg','Solver reasons by key-down', [str(x['strike'])+'/'+x['note_name'] for x in trials],
             [(name,[x[name+'_frames'] for x in trials]) for name in ('constraints_remaining','displacement_limited','idle_constraints')],
             unit='frame count',subtitle=caption+'; codes indicate observations, not established causes')
    side=[r for r in sides if start<=r['timestamp_sec']<end]
    line_svg(out/'08_skeleton_side_effects.svg','Skeleton side-effect diagnostics',side,
             [('Bone change (mm)',[('max_bone_change_mm','max absolute bone change')]),
              ('Bend excess (deg)',[('raw_max_bend_excess_deg','Raw'),('corrected_max_bend_excess_deg','FullRules')]),
              ('Non-key-down tips (mm)',[('nonpress_tip_max_displacement_mm','max tip displacement; includes releasing Index')])],start,end,time_caption)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('session');p.add_argument('--output',required=True);p.add_argument('--onset-first',type=int,required=True)
    p.add_argument('--onset-last',type=int,required=True);p.add_argument('--generation',type=int,required=True)
    p.add_argument('--source',default='BLE');p.add_argument('--hand',choices=('Right','Left'),default='Right')
    p.add_argument('--finger',choices=base.FINGERS,default='Index');p.add_argument('--settings-revision',type=int,required=True)
    args=p.parse_args();session=Path(args.session).resolve();out=Path(args.output).resolve()
    base.require(not out.exists() and out != session and session not in out.parents and out not in session.parents,'Output must be new and separate')
    data=base.load_session(session)
    base.require(data['meta'].get('session_id') and data['meta'].get('execution_mode')=='DistributedQuestClient','Unexpected session')
    # Check the formal validator exactly as it stands, without asserting video confirmation.
    try:
        base.trial_frames(data,dict(start_sec=0,end_sec=1,task='five_finger',video_verified=False))
    except ValueError as e:formal_rejection=str(e)
    else:raise ValueError('Formal eligibility unexpectedly accepted unverified fingering')
    spans=paired_spans(data['events'],args.source,args.generation,args.onset_first,args.onset_last)
    rows,sides=extract_frames(data,spans,args.hand,args.finger,args.settings_revision)
    trials,summary=summarize(rows,spans)
    out.mkdir(parents=True)
    write_csv(out/'paired_frames.csv',rows);write_csv(out/'strike_summary.csv',trials)
    write_csv(out/'axis_summary.csv',summary);write_csv(out/'side_effects_per_frame.csv',sides)
    write_csv(out/'midi_intervals.csv',spans)
    write_csv(out/'release_summary.csv',release_summary(rows,sides,spans))
    write_csv(out/'nonpressing_finger_summary.csv',finger_summary(data,[r for r in rows if r['timestamp_sec']<spans[-1]['off']],args.hand,args.finger))
    reasons=Counter((r['reason'],r['state'],r['strike']) for r in rows if r['timestamp_sec']<spans[-1]['off'])
    write_csv(out/'reason_by_state_and_strike.csv',[dict(reason=k[0],state=k[1],strike=k[2],frames=n) for k,n in sorted(reasons.items(),key=lambda x:str(x[0]))])
    figures(out,rows,sides,trials,summary,spans)
    source_hashes=data['source_hashes']
    base.require(all(hashlib.sha256(Path(k).read_bytes()).hexdigest()==v for k,v in source_hashes.items()),'Input changed during analysis')
    manifest=dict(analysis='exploratory',video_verified=False,actual_finger_evidence='participant self-report only',formal_rejection=formal_rejection,
        formal_additional_ineligibility='manualNoteMap=false; formal pipeline requires manual map',session_id=data['meta']['session_id'],
        input_sha256=source_hashes,tool_sha256={x:hashlib.sha256(Path(__file__).with_name(x).read_bytes()).hexdigest() for x in
        ('explore_single_finger_session.py','analyze_rule_effect.py','analyze_raw_session.py','evaluate_pilot.py')},
        source=args.source,generation=args.generation,finger=args.hand+' '+args.finger,settings_revision=args.settings_revision,
        onset_sec=spans[0]['on'],off_sec=spans[-1]['off'],strikes=len(spans),key_down_valid_frames=sum(x['valid_frames'] for x in trials),
        classification_tolerance_mm=EPS_MM,movement_threshold_mm=MOVEMENT_MM,
        denominator='all valid paired callbacks in observed MIDI [Note On, Note Off), including non-applied correction',
        mean='frame-weighted paired mean; strike means and SD supplied separately',
        definition='XZ nearest point on union of white-key surface rectangles minus black-key footprints; Y signed and absolute rest-plane distance',
        warning='One participant, one session, 16 dependent keystrokes; geometry consistency is not external ground truth or physical contact accuracy',
        jitter='not evaluated: no experimenter-labelled static interval; moving-performance derivatives are not jitter',
        processing_ms='logger CPU timer excluding logging and display; not end-to-end latency',
        logger_safety_metrics='approximate capsule interference and key segment penetration; logger diagnostics, not independent physical truth')
    (out/'analysis_manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False,allow_nan=False),encoding='utf-8')
    print(json.dumps(dict(output=str(out),strikes=len(spans),valid_frames=manifest['key_down_valid_frames'],summary=summary),ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
