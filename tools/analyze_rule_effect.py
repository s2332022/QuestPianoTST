"""Immutable Raw / FullRules paired evaluation. Python standard library only.

All computations use KeyboardRootLocal metres. SVG/table presentation uses mm.
No external ground truth, physical contact inference, or automatic fingering.
"""
import argparse
import hashlib
import html
import json
import math
import random
import statistics as st
import sys
import textwrap
from collections import Counter, defaultdict
from pathlib import Path

from analyze_raw_session import CHAINS, derive, read_csv, truth, write_csv, xyz

FINGERS = ('Thumb', 'Index', 'Middle', 'Ring', 'Little')
TASKS = ('white_single', 'black_single', 'five_finger', 'repeated', 'chord')
FILES = ('session_metadata.json', 'research_recording_summary.json',
         'transform_snapshots.jsonl', 'rule_pose_settings.jsonl',
         'quest_hand_raw.csv', 'quest_rule_pose.csv', 'quest_rule_pose_joints.csv',
         'quest_midi_research.csv')
AXES = ('x', 'y', 'z', 'band', 'float', 'penetration')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def finite(value):
    value = float(value)
    require(math.isfinite(value), 'Non-finite numeric value')
    return value


def unique(rows, key):
    result = {}
    for row in rows:
        k = key(row)
        require(k not in result, 'Duplicate record: ' + str(k))
        result[k] = row
    return result


def jsonl(path):
    return [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]


def black(note):
    return int(note) % 12 in (1, 3, 6, 8, 10)


def subtract(regions, cut):
    """Rectangle subtraction, independent of correction implementation."""
    result = []
    for a, b, c, d in regions:
        x0, z0, x1, z1 = max(a, cut[0]), max(b, cut[1]), min(c, cut[2]), min(d, cut[3])
        if x0 >= x1 or z0 >= z1:
            result.append((a, b, c, d))
        else:
            result.extend(r for r in ((a,b,x0,d), (x1,b,c,d), (x0,b,x1,z0), (x0,z1,x1,d))
                          if r[0] < r[2] and r[1] < r[3])
    return result


def surface(snapshot, note):
    keys = unique(snapshot['keys'], lambda k: int(k['note']))
    require(note in keys, 'Missing target key geometry')
    key = keys[note]
    lo, hi = xyz(key['min']), xyz(key['max'])
    require(all(math.isfinite(v) for v in lo+hi) and all(lo[i] < hi[i] for i in range(3)), 'Invalid key bounds')
    regions = [(lo[0], lo[2], hi[0], hi[2])]
    if not black(note):
        for n, k in sorted(keys.items()):
            if black(n):
                a, b = xyz(k['min']), xyz(k['max'])
                regions = subtract(regions, (a[0], a[2], b[0], b[2]))
    require(regions, 'Empty effective key surface')
    return hi[1], sorted(regions)


def errors(p, geometry, below, above):
    height, regions = geometry
    # Equal-distance ties: lexicographically smallest closest (X,Z), then rectangle.
    candidates = []
    for rect in regions:
        a,b,c,d = rect
        x, z = min(max(p[0], a), c), min(max(p[2], b), d)
        candidates.append(((p[0]-x)**2+(p[2]-z)**2, x, z, rect))
    _, x, z, _ = min(candidates)
    y = p[1]-height
    return dict(x=abs(p[0]-x), y=abs(y), z=abs(p[2]-z), signed_y=y,
                band=max(0., y-above, -below-y), float=max(y, 0.), penetration=max(-y, 0.))


def improvement(raw, corrected):
    delta = raw-corrected
    return delta, None if raw == 0 else delta/raw*100


def vec(row, prefix):
    return tuple(finite(row[prefix+a]) for a in 'xyz')


def event_key(e):
    return (e['source'], int(e.get('connection_generation', e.get('generation', 0))),
            int(e['event_index']))


def load_session(path):
    path = Path(path).resolve()
    for name in FILES:
        require((path/name).is_file(), 'Missing recording file: ' + str(path/name))
    source_hashes={str(path/name):hashlib.sha256((path/name).read_bytes()).hexdigest() for name in FILES}
    meta = json.loads((path/FILES[0]).read_text(encoding='utf-8-sig'))
    status = json.loads((path/FILES[1]).read_text(encoding='utf-8-sig'))
    require(status.get('writer_closed') is True and status.get('queue_overflows') == 0
            and not status.get('error'), 'Incomplete recording / writer error')
    snapshots = unique(jsonl(path/FILES[2]), lambda r: int(r['snapshot_id']))
    for snapshot in snapshots.values():
        for name in ('tracking_origin','keyboard_root'):
            m = [finite(v) for v in snapshot[name]['local_to_world']]
            require(len(m)==16, 'Invalid transform matrix')
            columns = [tuple(m[r*4+c] for r in range(3)) for c in range(3)]
            require(all(abs(sum(x*y for x,y in zip(columns[i],columns[j]))-float(i==j))<.0001
                        for i in range(3) for j in range(3)), 'Scaled/sheared transform: Keyboard local units are not rigid metres')
            x,y,z=columns
            determinant=x[0]*(y[1]*z[2]-y[2]*z[1])-y[0]*(x[1]*z[2]-x[2]*z[1])+z[0]*(x[1]*y[2]-x[2]*y[1])
            require(determinant>0 and all(abs(m[12+i]-v)<1e-6 for i,v in enumerate((0,0,0,1))), 'Invalid reflected/projective transform')
    settings = unique(jsonl(path/FILES[3]), lambda r: int(r['revision']))
    for s in settings.values():
        s['common'] = json.loads(s['common_settings_json'])
        s['rule'] = json.loads(s['rule_settings_json'])
    raw, rules, joints, events = [read_csv(path/name) for name in FILES[4:]]
    require(all(None not in r and all(v is not None for v in r.values()) for r in raw+rules+joints+events), 'Truncated or malformed CSV row')
    require(all(int(r.get('schema_version',1))==1 for r in rules+joints),'Unsupported rule log schema')
    require(raw and rules and joints, 'No FullRules paired recording; legacy-only data are ineligible')
    require(all(r['session_id'] == meta['session_id'] for r in raw+rules+joints+events), 'Mixed session IDs')
    unique(raw,lambda r:(int(r['callback_index']),r['hand'],r['joint_name']))
    callback_snapshots = {}
    raw_frames = defaultdict(set)
    for row in raw:
        cb,sid=int(row['callback_index']),int(row['snapshot_id'])
        require(row['update_type']=='Dynamic','Non-Dynamic Raw observation')
        require(cb not in callback_snapshots or callback_snapshots[cb]==sid,'Mixed snapshots within callback')
        require(finite(snapshots[sid]['quest_timestamp_sec']) <= finite(row['quest_timestamp_sec'])+1e-6,'Future transform snapshot')
        callback_snapshots[cb]=sid
        raw_frames[(cb,row['hand'])].add(row['joint_name'])
    hand_schemas = {}
    for (cb,hand), names in raw_frames.items():
        require(hand not in hand_schemas or hand_schemas[hand]==names,'Incomplete Raw joint frame')
        hand_schemas[hand]=names
    raw = derive(raw, snapshots)
    raw_index = unique(raw, lambda r: (int(r['callback_index']), r['hand'], r['joint_name']))
    rule_index = unique(rules, lambda r: (int(r['callback_index']), r['hand'], r['finger']))
    joint_index = unique(joints, lambda r: (int(r['callback_index']), r['hand'], r['joint_name']))
    joint_frames = defaultdict(dict)
    for (cb, hand, name), row in joint_index.items():
        joint_frames[(cb,hand)][name] = row
    callbacks = unique([dict(callback=k, timestamp=v) for k,v in
                        {int(r['callback_index']): r['timestamp'] for r in raw}.items()], lambda r: r['callback'])
    events.sort(key=lambda r: (finite(r['quest_observed_timestamp_sec']), event_key(r)))
    event_index = unique(events, event_key)
    for e in events:
        require(int(e['snapshot_id']) in snapshots, 'Missing MIDI snapshot')
        require(finite(e['quest_observed_timestamp_sec']) >= finite(e['quest_receive_timestamp_sec']), 'MIDI observation precedes receive')
    return dict(path=path, meta=meta, snapshots=snapshots, settings=settings, raw=raw_index,
                rules=rule_index, joints=joint_index, joint_frames=joint_frames, raw_frames=raw_frames, callbacks=callbacks, events=events, event_index=event_index, geometry_cache={},source_hashes=source_hashes)


def recorded_surface(data, sid, note):
    key=(int(sid),int(note))
    if key not in data['geometry_cache']:
        data['geometry_cache'][key]=surface(data['snapshots'][key[0]],key[1])
    return data['geometry_cache'][key]


def check_pair(raw, row, trial, expected_method='FullRules'):
    require(row['coordinate_space'] == 'KeyboardRootLocal' and row['position_unit'] == 'm', 'Wrong coordinate space/unit')
    require(int(row['snapshot_id']) == int(raw['snapshot_id']), 'Snapshot mismatch')
    require(abs(finite(row['quest_timestamp_sec'])-raw['timestamp']) <= 1e-6, 'Callback timestamp mismatch')
    require(int(row['settings_revision']) == int(trial['settings_revision']), 'Settings changed within trial')
    require(row['requested_method'] == expected_method and row['executed_method'] == expected_method, 'Trial is not '+expected_method)
    if 'joint_id' in row:
        require(int(row['joint_id'])==int(raw['joint_id']),'Joint ID mismatch')
    if 'unity_frame' in raw and 'unity_frame' in row:
        require(int(row['unity_frame'])==int(raw['unity_frame']),'Unity frame mismatch')
    if raw['tracking_valid'] and truth(row.get('pose_valid', row.get('coordinates_valid'))):
        require(max(abs(raw['keyboard_'+a]-finite(row['raw_'+a])) for a in 'xyz') < 2e-6,
                'Raw reconstruction disagrees with paired row')


def active_events(data, start, end):
    """Observed MIDI state, sustain does not extend physical key-down intervals."""
    held, spans, generations = {}, [], {}
    for e in data['events']:
        t = finite(e['quest_observed_timestamp_sec'])
        source, generation, _ = event_key(e)
        if t > end:
            break
        reset = truth(e['synthetic']) or (source in generations and generations[source] != generation)
        channel_reset = e['event_type'] == 'ControlChange' and int(e['control']) in (120, 123)
        if reset or channel_reset:
            for k in list(held):
                if k[0] == source and (reset or k[2] == int(e['channel'])):
                    onset, begin = held.pop(k)
                    spans.append((onset, begin, t))
        generations[source] = generation
        if truth(e['synthetic']):
            continue
        key = (source, generation, int(e['channel']), int(e['note']))
        on = e['event_type'] == 'NoteOn' and int(e['velocity']) > 0
        off = e['event_type'] == 'NoteOff' or e['event_type'] == 'NoteOn' and int(e['velocity']) == 0
        if on:
            if key in held:
                onset, begin = held.pop(key)
                spans.append((onset, begin, t))
            held[key] = (e, t)
        elif off and key in held:
            onset, begin = held.pop(key)
            spans.append((onset, begin, t))
    spans.extend((e, begin, end) for e,begin in held.values())
    return [(e,a,b) for e,a,b in spans if a < end and b > start]


def trial_frames(data, trial):
    start, end = finite(trial['start_sec']), finite(trial['end_sec'])
    require(start < end, 'Invalid trial interval')
    require(trial['task'] in TASKS and trial['video_verified'] is True, 'Unknown task or fingering not video verified')
    require(trial.get('calibration_verified') is True,'Calibration not operator verified')
    settings = data['settings'][int(trial['settings_revision'])]
    times = [f['timestamp'] for f in data['callbacks'].values()]
    require(min(times) <= start < end <= max(times), 'Trial extends outside recorded callbacks')
    require(finite(settings['quest_timestamp_sec']) <= start, 'Settings revision starts after trial')
    require(settings['configuration_valid'] is True, 'Invalid logged configuration')
    common, rule = settings['common'], settings['rule']
    require(all(rule[k] is True for k in ('contact','joints','nonpenetration','interference')), 'FullRules ablation is not primary condition')
    require(rule['manualNoteMap'] is True, 'Manual note map required')
    expected_hand = 'Left' if common['leftHand'] else 'Right'
    assignments = unique(trial['assignments'], lambda a: (a['source'], int(a['generation']), int(a['onset_event_index'])))
    spans = active_events(data, start, end)
    require(spans, 'Trial has no observed key-down events')
    for e,a,b in spans:
        require(event_key(e) in assignments, 'Missing video-confirmed actual fingering for MIDI event ' + str(event_key(e)))
    rows, trajectories = [], []
    for e,begin,finish in spans:
        assignment = assignments[event_key(e)]
        hand, finger = assignment['hand'], assignment['finger']
        require(hand == expected_hand and finger in FINGERS, 'Wrong hand / finger')
        note = int(e['note'])
        require(int(assignment['note']) == note, 'Assigned note mismatch')
        for cb, frame in sorted(data['callbacks'].items()):
            t = frame['timestamp']
            if not (max(start, begin-.25) <= t < min(end, finish+.25)):
                continue
            raw = data['raw'].get((cb, hand, finger+'Tip'))
            pair = data['joints'].get((cb, hand, finger+'Tip'))
            contact = data['rules'].get((cb, hand, finger))
            require(raw is not None and pair is not None and contact is not None, 'Missing paired callback/joint/contact')
            check_pair(raw, pair, trial)
            check_pair(raw, contact, trial)
            require(contact['calibration_id'] == trial['calibration_id'], 'Calibration mismatch')
            valid = raw['tracking_valid'] and truth(pair['pose_valid']) and truth(contact['coordinates_valid'])
            row = dict(participant=trial['participant'], trial_id=trial['trial_id'], task=trial['task'],
                       session_id=data['meta']['session_id'], callback_index=cb, snapshot_id=raw['snapshot_id'],
                       settings_revision=trial['settings_revision'], calibration_id=trial['calibration_id'],
                       timestamp=t, hand=hand, finger=finger, note=note, key_type='black' if black(note) else 'white',
                       source=e['source'], generation=e['connection_generation'], onset_event_index=e['event_index'],
                       contact_event_index=contact['event_index'],contact_onset_event_index=contact['onset_event_index'],
                       receive_sec=e['quest_receive_timestamp_sec'], observed_sec=begin,
                       relative_observed_ms=(t-begin)*1000, relative_receive_ms=(t-finite(e['quest_receive_timestamp_sec']))*1000,
                       state=contact['state'], reason=contact['reason'], contact_reason=contact['contact_reason'],
                       valid=valid, applicable=False, processing_ms=contact.get('processing_ms',''),
                       target_capped=contact['target_capped'], solver_limited=contact['solver_limited'])
            mapped = rule['noteToFinger'][note] == FINGERS.index(finger)
            matched = (int(contact['note']) == note and contact['source'] == e['source'] and
                       int(contact['generation']) == int(e['connection_generation']) and
                       int(contact['channel']) == int(e['channel']) and int(contact['onset_event_index']) == int(e['event_index']))
            if matched:
                referenced=data['event_index'].get((contact['source'],int(contact['generation']),int(contact['event_index'])))
                require(referenced is not None,'Missing referenced MIDI event')
                require(int(referenced['note'])==note and int(referenced['channel'])==int(e['channel'])
                        and finite(referenced['quest_observed_timestamp_sec']) <= t+1e-6,'Invalid or future MIDI reference')
            row['applicable'] = valid and mapped and matched and truth(contact['enabled']) and truth(contact['solver_finger_valid']) and finite(contact['gain']) > 0
            row['failure'] = ('tracking_or_coordinates_invalid' if not valid else 'manual_map_mismatch' if not mapped
                              else 'contact_not_assigned_or_conflict' if not matched else
                              contact['reason'] or contact['contact_reason'] or 'not_applied') if not row['applicable'] else ''
            if valid:
                offset = tuple(x+y for x,y in zip(xyz(common['tipToContactKeyboardMeters']), xyz(rule['fingers'][FINGERS.index(finger)]['contactOffsetKeyboardMeters'])))
                geometry = recorded_surface(data,raw['snapshot_id'],note)
                below, above = finite(common['belowSurfaceToleranceMeters']), finite(common['aboveSurfaceToleranceMeters'])
                require(below >= 0 and above >= 0, 'Negative normal tolerance')
                for label,p in (('raw', tuple(raw['keyboard_'+a] for a in 'xyz')), ('corrected', vec(pair,'corrected_'))):
                    require(max(abs(p[i]-finite(contact[label+'_'+a])) for i,a in enumerate('xyz')) < 2e-6, 'Contact/joint position mismatch')
                    p = tuple(v+o for v,o in zip(p, offset))
                    row.update({label+'_pad_'+a: p[i] for i,a in enumerate('xyz')})
                    row.update({label+'_'+k+'_m': v for k,v in errors(p,geometry,below,above).items()})
                for axis in AXES:
                    delta, rate = improvement(row['raw_'+axis+'_m'], row['corrected_'+axis+'_m'])
                    row['delta_'+axis+'_m'], row['rate_'+axis+'_pct'] = delta, rate
            trajectories.append(row)
            if begin <= t < finish and start <= t < end:
                rows.append(row)
    require(rows, 'No hand callbacks during key-down')
    return rows, trajectories


def percentile(values, p):
    s = sorted(values)
    x = (len(s)-1)*p
    i = int(x)
    return s[i]+(s[min(i+1,len(s)-1)]-s[i])*(x-i)


def bootstrap(trials, field, seed=20261027, repeats=2000):
    """Equal participant weight; resample participants, then their trials."""
    groups = defaultdict(list)
    for r in trials:
        if r.get(field) is not None:
            groups[r['participant']].append(r[field])
    if sum(map(len, groups.values())) < 2:
        return None, None
    rng, values, participants = random.Random(seed), [], sorted(groups)
    for _ in range(repeats):
        means = []
        for p in rng.choices(participants, k=len(participants)):
            v = groups[p]
            means.append(st.mean(rng.choices(v,k=len(v))))
        values.append(st.mean(means))
    return percentile(values,.025), percentile(values,.975)


def summarize(rows):
    groups = defaultdict(list)
    for r in rows:
        groups[(r['participant'],r['trial_id'])].append(r)
    trials = []
    for (participant,tid), group in sorted(groups.items()):
        for dimension in ('all','finger','key_type','task'):
            subsets = defaultdict(list)
            for r in group:
                subsets[r[dimension] if dimension != 'all' else 'all'].append(r)
            for label, subset in sorted(subsets.items()):
                valid = [r for r in subset if r['valid']]
                for axis in AXES:
                    row = dict(participant=participant,trial_id=tid,group=dimension,label=label,axis=axis,
                               expected_frames=len(subset),valid_frames=len(valid),
                               applicable_rate=st.mean(r['applicable'] for r in subset))
                    for condition in ('raw','corrected'):
                        v = [r[condition+'_'+axis+'_m']*1000 for r in valid]
                        row[condition+'_mean_mm'] = st.mean(v) if v else None
                        row[condition+'_median_mm'] = st.median(v) if v else None
                        row[condition+'_sd_mm'] = st.stdev(v) if len(v)>1 else None
                    if valid:
                        deltas = [r['delta_'+axis+'_m']*1000 for r in valid]
                        row.update(delta_mean_mm=st.mean(deltas), delta_median_mm=st.median(deltas),
                                   delta_sd_mm=st.stdev(deltas) if len(deltas)>1 else None,
                                   improved_rate=st.mean(v>1e-6 for v in deltas), worsened_rate=st.mean(v < -1e-6 for v in deltas),
                                   unchanged_rate=st.mean(abs(v)<=1e-6 for v in deltas))
                        _, row['ratio_of_means_pct'] = improvement(row['raw_mean_mm'],row['corrected_mean_mm'])
                        rates = [r['rate_'+axis+'_pct'] for r in valid if r['rate_'+axis+'_pct'] is not None]
                        row['frame_rate_mean_pct'] = st.mean(rates) if rates else None
                        row['undefined_frame_rates'] = len(valid)-len(rates)
                    trials.append(row)
    summaries = []
    grouped = defaultdict(list)
    for r in trials:
        grouped[(r['group'],r['label'],r['axis'])].append(r)
    for (dimension,label,axis), group in sorted(grouped.items()):
        valid = [r for r in group if r['valid_frames']]
        row = dict(group=dimension,label=label,axis=axis,trials=len(group),valid_trials=len(valid),
                   participants=len({r['participant'] for r in valid}),
                   frames=sum(r['expected_frames'] for r in group), valid_frames=sum(r['valid_frames'] for r in group),
                   exploratory=True, method='Raw / FullRules; paired trial means; equal participant weight')
        fields = ('raw_mean_mm','corrected_mean_mm','delta_mean_mm','ratio_of_means_pct',
                  'applicable_rate','improved_rate','worsened_rate','unchanged_rate')
        for field in fields:
            participants = defaultdict(list)
            for r in group:
                if r.get(field) is not None:
                    participants[r['participant']].append(r[field])
            v = [st.mean(x) for x in participants.values()]
            row[field] = st.mean(v) if v else None
            row[field+'_ci_low'],row[field+'_ci_high'] = bootstrap(group,field)
        deltas = [r['delta_mean_mm'] for r in valid]
        row['trial_delta_median_mm'] = st.median(deltas) if deltas else None
        row['trial_delta_sd_mm'] = st.stdev(deltas) if len(deltas)>1 else None
        row['worsened_trial_rate'] = st.mean(v < -1e-6 for v in deltas) if deltas else None
        summaries.append(row)
    return trials,summaries


def distance(a,b):
    return math.sqrt(sum((x-y)**2 for x,y in zip(a,b)))


def observed_states(data, t):
    """Only observed owners; absent NoteOffs never prove physical release."""
    states, generations = {}, {}
    for e in data['events']:
        if finite(e['quest_observed_timestamp_sec']) > t:
            break
        source, generation, _ = event_key(e)
        if truth(e['synthetic']) or source in generations and generations[source] != generation:
            states = {k:v for k,v in states.items() if k[0] != source}
        generations[source] = generation
        if truth(e['synthetic']):
            continue
        key=(source,int(e['channel']),int(e['note']))
        if e['event_type'] in ('NoteOn','NoteOff'):
            states[key]=e['event_type']=='NoteOn' and int(e['velocity'])>0
        elif e['event_type']=='ControlChange' and int(e['control']) in (120,123):
            for n in range(128):
                states[(source,int(e['channel']),n)]=False
    return states


def side_effects(data, trial, main):
    """Independent joint displacement, bone lengths, bend, motion derivatives.

    Logger's capsule/penetration fields are separately exported as diagnostics.
    """
    result, previous = [], {}
    by_cb = defaultdict(set)
    for r in main:
        by_cb[r['callback_index']].add(r['finger'])
    hand = 'Left' if data['settings'][int(trial['settings_revision'])]['common']['leftHand'] else 'Right'
    config = data['settings'][int(trial['settings_revision'])]['rule']
    for cb, frame in sorted(data['callbacks'].items()):
        t = frame['timestamp']
        if not trial['start_sec'] <= t < trial['end_sec']:
            continue
        joints = {}
        expected = data['raw_frames'][(cb,hand)]
        require(expected == set(data['joint_frames'][(cb,hand)]), 'Incomplete corrected joint frame')
        for name, pair in data['joint_frames'][(cb,hand)].items():
            h = hand
            raw = data['raw'].get((cb,h,name))
            require(raw is not None, 'Joint without Raw')
            check_pair(raw,pair,trial)
            if not raw['tracking_valid'] or not truth(pair['pose_valid']):
                previous.pop(name,None)
                continue
            a,b = tuple(raw['keyboard_'+x] for x in 'xyz'),vec(pair,'corrected_')
            joints[name] = (a,b)
            base = dict(participant=trial['participant'],trial_id=trial['trial_id'],timestamp=t,
                        callback_index=cb,hand=h,joint=name,snapshot_id=pair['snapshot_id'])
            row = dict(base,metric='displacement',raw=0.,corrected=distance(a,b)*1000,unit='mm',
                       nonpressing=not any(name.startswith(f) for f in by_cb.get(cb,set())))
            result.append(row)
            prev = previous.get(name)
            now = dict(t=t,sid=pair['snapshot_id'],a=a,b=b)
            if prev and prev['sid'] == now['sid'] and 0 < t-prev['t'] <= .1:
                dt = t-prev['t']
                va = tuple((x-y)/dt for x,y in zip(a,prev['a']))
                vb = tuple((x-y)/dt for x,y in zip(b,prev['b']))
                now.update(va=va,vb=vb)
                result.append(dict(base,metric='speed',raw=distance(va,(0,0,0))*1000,corrected=distance(vb,(0,0,0))*1000,unit='mm/s'))
                if 'va' in prev:
                    result.append(dict(base,metric='acceleration',raw=distance(va,prev['va'])/dt*1000,
                                       corrected=distance(vb,prev['vb'])/dt*1000,unit='mm/s^2'))
            previous[name] = now
        for a,b in CHAINS:
            if a in joints and b in joints:
                lengths = [distance(joints[a][i],joints[b][i])*1000 for i in range(2)]
                result.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],timestamp=t,callback_index=cb,
                                   joint=a+'/'+b,metric='bone_length',raw=lengths[0],corrected=lengths[1],unit='mm'))
                result.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],timestamp=t,callback_index=cb,
                                   joint=a+'/'+b,metric='bone_length_change',raw=0.,corrected=abs(lengths[1]-lengths[0]),unit='mm'))
        states=observed_states(data,t)
        for name,(raw,corrected) in joints.items():
            if not name.endswith('Tip'):
                continue
            f=next((f for f in FINGERS if name.startswith(f)),None)
            if f is None:
                continue
            common=data['settings'][int(trial['settings_revision'])]['common']
            offset=tuple(x+y for x,y in zip(xyz(common['tipToContactKeyboardMeters']),xyz(config['fingers'][FINGERS.index(f)]['contactOffsetKeyboardMeters'])))
            sid=int(data['joints'][(cb,hand,name)]['snapshot_id'])
            penetrations=defaultdict(list)
            for key in data['snapshots'][sid]['keys']:
                note=int(key['note'])
                owners=[v for (s,c,n),v in states.items() if n==note]
                state='pressed' if any(owners) else 'observed_released' if owners else 'unknown'
                if state=='pressed':
                    continue
                geometry=recorded_surface(data,sid,note)
                depths=[]
                for p in (raw,corrected):
                    contact=tuple(x+y for x,y in zip(p,offset))
                    error=errors(contact,geometry,0,0)
                    depths.append(error['penetration']*1000 if error['x']==0 and error['z']==0 else 0.)
                penetrations[state].append(depths)
            for state,depths in penetrations.items():
                result.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],timestamp=t,callback_index=cb,
                                   joint=name,midi_state=state,metric=state+'_tip_penetration',
                                   raw=max(v[0] for v in depths),corrected=max(v[1] for v in depths),unit='mm',
                                   evidence='maximum point penetration over effective rest planes; includes zeros; observed owners only; unknown separately'))
        for f in FINGERS:
            chain = ([f+'Metacarpal',f+'Proximal',f+'Distal',f+'Tip'] if f=='Thumb' else
                     [f+'Proximal',f+'Intermediate',f+'Distal',f+'Tip'])
            guide = 'Wrist' if f=='Thumb' else f+'Metacarpal'
            chain = [guide]+chain
            limits = xyz(config['fingers'][FINGERS.index(f)]['maximumBendDegrees'])
            for j in range(3):
                names = chain[j:j+3]
                if all(n in joints for n in names):
                    angles = []
                    for i in range(2):
                        a,b,c = [joints[n][i] for n in names]
                        u,v = tuple(y-x for x,y in zip(a,b)),tuple(y-x for x,y in zip(b,c))
                        norm = distance(u,(0,0,0))*distance(v,(0,0,0))
                        angles.append(max(0.,math.degrees(math.acos(max(-1.,min(1.,sum(x*y for x,y in zip(u,v))/norm))))-limits[j]) if norm>1e-12 else None)
                    result.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],timestamp=t,callback_index=cb,
                                       joint=names[1],metric='bend_excess',raw=angles[0],corrected=angles[1],unit='deg'))
    # Static intervals must be labelled by the experimenter, never inferred from motion.
    for index,interval in enumerate(trial.get('static_intervals',[])):
        a,b = map(finite,interval)
        require(trial['start_sec'] <= a < b <= trial['end_sec'], 'Invalid labelled static interval')
        grouped = defaultdict(list)
        for (cb,h,name),pair in data['joints'].items():
            raw=data['raw'][(cb,h,name)]
            if h==hand and a <= raw['timestamp'] < b and raw['tracking_valid'] and truth(pair['pose_valid']):
                grouped[(name,pair['snapshot_id'])].append(pair)
        for (name,sid),pairs in grouped.items():
            for axis in 'xyz':
                if len(pairs)>1:
                    result.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],joint=name,
                                       metric='static_jitter',interval=index,snapshot_id=sid,axis=axis,unit='mm',
                                       raw=st.stdev(finite(r['raw_'+axis])*1000 for r in pairs),
                                       corrected=st.stdev(finite(r['corrected_'+axis])*1000 for r in pairs)))
    return result


def table(path, rows, default):
    fields = list(dict.fromkeys(k for r in rows for k in r)) or default
    write_csv(path,fields,rows)


def svg(path, title, labels=(), series=(), unit='mm', subtitle='NO MEASURED DATA; template only'):
    """Standalone vector publication artifact; gaps never become zero results."""
    if not labels:
        labels=['No samples']
        series=[(name,[None]) for name,_ in series]
    w,h = max(1100,len(labels)*110),700
    caption = textwrap.wrap(subtitle,width=max(80,int((w-120)/7)))
    h += max(0,len(caption)-2)*18
    palette = ('#2166ac','#b2182b','#238b45')
    values = [v for _,vals in series for v in vals if v is not None]
    lo,hi = min([0.]+values),max([0.]+values)
    if hi==lo:
        hi=lo+1
    span=hi-lo
    lo-=span*.12
    hi+=span*.12
    y = lambda v: 485-(v-lo)/(hi-lo)*300
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">',
             '<rect width="100%" height="100%" fill="white"/>',
             '<g font-family="sans-serif" fill="#222">',
             f'<text x="60" y="35" font-size="22">{html.escape(title)}</text>',
             f'<text x="20" y="155" font-size="15">{html.escape(unit)}</text>']
    for i in range(6):
        v=lo+(hi-lo)*i/5
        parts.extend([f'<path d="M90 {y(v):.2f} H{w-40}" stroke="#ddd"/>',
                      f'<text x="15" y="{y(v)+5:.2f}" font-size="13">{v:.3g}</text>'])
    for i,label in enumerate(labels):
        x=110+(i+.5)*(w-180)/max(1,len(labels))
        for k,line in enumerate(textwrap.wrap(str(label),width=15)):
            parts.append(f'<text x="{x:.2f}" y="{515+k*15}" text-anchor="middle" font-size="12">{html.escape(line)}</text>')
        for j,(name,vals) in enumerate(series):
            v=vals[i]
            if v is None:
                continue
            bx=x+(j-(len(series)-1)/2)*20
            parts.append(f'<rect x="{bx-8:.2f}" y="{min(y(v),y(0)):.2f}" width="16" height="{max(.5,abs(y(v)-y(0))):.2f}" fill="{palette[j%3]}"/>')
    for j,(name,_) in enumerate(series):
        parts.append(f'<text x="{90+j*300}" y="585" fill="{palette[j%3]}" font-size="15">{html.escape(name)}</text>')
    for k,line in enumerate(caption):
        parts.append(f'<text x="60" y="{625+k*18}" font-size="13">{html.escape(line)}</text>')
    parts.append('</g></svg>')
    path.write_text('\n'.join(parts),encoding='utf-8')


def figures(output, summaries, sides, trajectories):
    for dimension,filename,title in (('all','01_xyz_comparison','XYZ distance to effective rest key surface'),
                                      ('all','02_xyz_improvement','XYZ improvement (Raw - FullRules)'),
                                      ('finger','03_finger','Finger improvement'),('key_type','04_key_type','White / black key improvement'),
                                      ('task','05_task','Task improvement')):
        rows = [r for r in summaries if r['group']==dimension and r['axis'] in 'xyz']
        labels = [r['label']+'/'+r['axis'].upper() for r in rows]
        if not rows:
            categories={'all':['all'],'finger':FINGERS,'key_type':['white','black'],'task':TASKS}[dimension]
            labels=[label+'/'+axis.upper() for label in categories for axis in 'xyz']
        fields = ('raw_mean_mm','corrected_mean_mm') if filename.startswith('01') else ('delta_mean_mm',)
        names = ('Raw','FullRules') if len(fields)==2 else ('Raw - FullRules',)
        subtitle = ('Exploratory; participant-weighted paired trial means; '+ '; '.join(
            f"{r['label']}/{r['axis']}: P={r['participants']}, trials={r['valid_trials']}/{r['trials']}, pairs={r['valid_frames']}/{r['frames']}" for r in rows)) if rows else 'NO MEASURED DATA; template only'
        # Full counts/conditions and CIs also appear in the adjacent CSV.
        svg(output/(filename+'.svg'),title,labels,[(n,[r.get(f) for r in rows] if rows else [None]*len(labels)) for n,f in zip(names,fields)],subtitle=subtitle)
    for metric,unit in (('bone_length','mm'),('bone_length_change','mm'),('bend_excess','deg'),('displacement','mm'),('speed','mm/s'),('acceleration','mm/s^2'),('static_jitter','mm'),('logger_interference','mm'),('logger_penetration','mm'),('nonpressing_movement','mm'),('wrist_palm_movement','mm'),('observed_released_tip_penetration','mm'),('unknown_tip_penetration','mm')):
        source_metric='displacement' if metric in ('nonpressing_movement','wrist_palm_movement') else metric
        rows = [r for r in sides if r['metric']==source_metric and r.get('raw') is not None and r.get('corrected') is not None
                and (metric!='nonpressing_movement' or r.get('nonpressing') and r['joint'] not in ('Wrist','Palm'))
                and (metric!='wrist_palm_movement' or r['joint'] in ('Wrist','Palm'))]
        groups=defaultdict(list)
        for r in rows:
            groups[(r['participant'],r['trial_id'])].append(r)
        labels=[p+'/'+t for p,t in groups]
        svg(output/('06_'+metric+'.svg'),metric+' (trial means)',labels,
            [(c,[st.mean(r[c] for r in group) for group in groups.values()]) for c in ('raw','corrected')],unit,
            f'Raw / FullRules; trial count={len(groups)}; joint samples={len(rows)}' if rows else 'NO MEASURED DATA; template only')
    # Separate event trajectory files: lines are genuine time series, split at gaps/snapshot changes.
    grouped=defaultdict(list)
    for r in trajectories:
        grouped[(r['participant'],r['trial_id'],r['source'],r['generation'],r['onset_event_index'])].append(r)
    if not grouped:
        svg(output/'07_event_trajectory.svg','MIDI event trajectories; X/Y/Z separate; observed time (ms)')
    for index,(key,rows) in enumerate(sorted(grouped.items())):
        rows.sort(key=lambda r:r['timestamp'])
        parts=['<svg xmlns="http://www.w3.org/2000/svg" width="1100" height="800"><rect width="100%" height="100%" fill="white"/>',
               '<g font-family="sans-serif" font-size="14">',f'<text x="40" y="25">Raw / FullRules; event {html.escape(str(key))}; pairs={sum(r["valid"] for r in rows)}/{len(rows)}</text>']
        for j,axis in enumerate('xyz'):
            good=[r for r in rows if r['valid']]
            if not good:
                continue
            vals=[r[c+'_pad_'+axis]*1000 for r in good for c in ('raw','corrected')]
            lo,hi=min(vals),max(vals)
            hi=max(hi,lo+1)
            times=[r['relative_observed_ms'] for r in rows]
            t0,t1=min(times),max(times)
            t1=max(t1,t0+1)
            y=lambda v: 220+j*240-(v-lo)/(hi-lo)*160
            x=lambda v: 100+(v-t0)/(t1-t0)*900
            parts.append(f'<text x="30" y="{60+j*240}">{axis.upper()} (mm), {lo:.2f} to {hi:.2f}</text>')
            for c,color in (('raw','#2166ac'),('corrected','#b2182b')):
                prev=None
                for r in rows:
                    if r['valid'] and prev and prev['snapshot_id']==r['snapshot_id'] and r['timestamp']-prev['timestamp']<=.1:
                        parts.append(f'<path d="M{x(prev["relative_observed_ms"]):.2f} {y(prev[c+"_pad_"+axis]*1000):.2f} L{x(r["relative_observed_ms"]):.2f} {y(r[c+"_pad_"+axis]*1000):.2f}" stroke="{color}" fill="none"/>')
                    prev=r if r['valid'] else None
            parts.append(f'<text x="100" y="{240+j*240}">{t0:.1f} to {t1:.1f} ms relative to Unity MIDI observation; blue Raw / red FullRules</text>')
        parts.append('</g></svg>')
        (output/f'07_event_{index:04d}.svg').write_text('\n'.join(parts),encoding='utf-8')


def analyze(conditions, output):
    conditions,output=Path(conditions).resolve(),Path(output).resolve()
    config=json.loads(conditions.read_text(encoding='utf-8-sig'))
    require(config['schema_version']==1, 'Unsupported conditions schema')
    require(not output.exists(), 'Output already exists; choose a new immutable run directory')
    trials=config['trials']
    unique(trials,lambda t:(t['participant'],t['trial_id']))
    datasets={}
    for trial in trials:
        path=(conditions.parent/trial['session_dir']).resolve()
        require(output != path and path not in output.parents and output not in path.parents, 'Output overlaps input')
        if path not in datasets:
            datasets[path]=load_session(path)
    for path in datasets:
        intervals=sorted((finite(t['start_sec']),finite(t['end_sec'])) for t in trials if (conditions.parent/t['session_dir']).resolve()==path)
        require(all(b<=c for (a,b),(c,d) in zip(intervals,intervals[1:])), 'Overlapping trials duplicate observations')
    frames,trajectories,sides=[],[],[]
    for trial in trials:
        data=datasets[(conditions.parent/trial['session_dir']).resolve()]
        require(data['meta']['session_id']==trial['session_id'],'Trial session ID mismatch')
        rows,tracks=trial_frames(data,trial)
        frames.extend(rows)
        trajectories.extend(tracks)
        sides.extend(side_effects(data,trial,rows))
    trial_stats,summary=summarize(frames)
    output.mkdir(parents=True)
    (output/'conditions_snapshot.json').write_text(json.dumps(config,indent=2,ensure_ascii=False,allow_nan=False),encoding='utf-8')
    empty_frame=['participant','trial_id','task','session_id','callback_index','snapshot_id','hand','finger','note','key_type',
                 'timestamp','source','generation','onset_event_index','settings_revision','calibration_id','valid','applicable','failure']
    empty_frame += [c+'_'+axis+'_m' for c in ('raw','corrected','delta') for axis in AXES]
    empty_frame += ['raw_signed_y_m','corrected_signed_y_m']+['rate_'+axis+'_pct' for axis in AXES]
    table(output/'paired_frames.csv',frames,empty_frame)
    table(output/'trial_statistics.csv',trial_stats,['participant','trial_id','axis','raw_mean_mm','corrected_mean_mm','delta_mean_mm'])
    table(output/'effect_summary.csv',summary,['group','label','axis','participants','valid_trials','raw_mean_mm','corrected_mean_mm','delta_mean_mm','delta_mean_mm_ci_low','delta_mean_mm_ci_high'])
    table(output/'event_trajectories.csv',trajectories,['participant','trial_id','relative_observed_ms','raw_pad_x','corrected_pad_x'])
    counts=Counter((r['participant'],r['trial_id'],r['failure'] or 'applied_with_possible_residual_constraints') for r in frames)
    totals=Counter((r['participant'],r['trial_id']) for r in frames)
    table(output/'applicability.csv',[dict(participant=p,trial_id=t,reason=reason,frames=n,expected_frames=totals[p,t],rate=n/totals[p,t]) for (p,t,reason),n in sorted(counts.items())],['participant','trial_id','reason','frames','expected_frames','rate'])
    diagnostics=[]
    for trial in trials:
        data=datasets[(conditions.parent/trial['session_dir']).resolve()]
        for row in data['rules'].values():
            if trial['start_sec'] <= finite(row['quest_timestamp_sec']) < trial['end_sec']:
                diagnostics.append(dict(row,participant=trial['participant'],trial_id=trial['trial_id'],evidence='solver/logger diagnostic; not independent primary endpoint'))
                for metric,prefix in (('logger_interference','interference'),('logger_penetration','penetration')):
                    if row.get(prefix+'_before_m') and row.get(prefix+'_after_m'):
                        sides.append(dict(participant=trial['participant'],trial_id=trial['trial_id'],metric=metric,unit='mm',
                                          raw=finite(row[prefix+'_before_m'])*1000,corrected=finite(row[prefix+'_after_m'])*1000,
                                          evidence='logger diagnostic; approximate capsules/key tops'))
    table(output/'solver_diagnostics.csv',diagnostics,['participant','trial_id','evidence'])
    cpu=unique([dict(participant=r['participant'],trial_id=r['trial_id'],callback_index=r['callback_index'],processing_ms=r['processing_ms'])
                for r in diagnostics if r.get('processing_ms') and r.get('finger')=='Thumb'],
               lambda r:(r['participant'],r['trial_id'],r['callback_index']))
    table(output/'processing_time.csv',list(cpu.values()),['participant','trial_id','callback_index','processing_ms'])
    cpu_groups=defaultdict(list)
    for r in cpu.values():
        cpu_groups[(r['participant'],r['trial_id'])].append(finite(r['processing_ms']))
    svg(output/'06_processing_time.svg','FullRules CPU elapsed (logger; excludes logging/rendering)',
        [p+'/'+t for p,t in cpu_groups],[('FullRules',[st.mean(v) for v in cpu_groups.values()])],unit='ms',
        subtitle=f'Callback count={len(cpu)}; not total Quest frame time or device load' if cpu else 'NO MEASURED DATA; template only')
    table(output/'side_effects.csv',sides,['participant','trial_id','metric','raw','corrected','unit'])
    figures(output,summary,sides,trajectories)
    hashes={p:h for data in datasets.values() for p,h in data['source_hashes'].items()}
    manifest=dict(schema_version=1,python_version=sys.version,measured=bool(frames),conditions_sha256=hashlib.sha256(conditions.read_bytes()).hexdigest(),
                  tool_sha256={n:hashlib.sha256(Path(__file__).with_name(n).read_bytes()).hexdigest() for n in ('analyze_rule_effect.py','analyze_raw_session.py')},
                  source_sha256=hashes,frames=len(frames),valid_frames=sum(r['valid'] for r in frames),
                  seed=20261027,bootstrap_repeats=2000,ci='percentile hierarchical participant/trial bootstrap; one trial: undefined; one participant: within-person exploratory',
                  primary='X/Z components to nearest point of union of effective rest key rectangles; Y absolute signed-plane distance; band separate',
                  tie='minimum (squared XZ distance, closest X, closest Z, rectangle tuple)',
                  rate='trial ratio of mean errors and mean defined frame ratios separately; zero Raw -> null',
                  limitation='Geometric display consistency only, not absolute hand tracking accuracy; MIDI is not physical contact; rest plane is not depressed physical key',
                  secondary='independent bone/bend/displacement/finite differences and tip-plane penetration by observed/unknown MIDI state; capsule interference/key segment penetration and CPU are logger diagnostics; no device load or perception claim',
                  temporal='observed MIDI key-down; .25 s event windows; receive and observe distinguished; no derivatives across invalid poses/snapshot changes or gaps >100ms')
    (output/'analysis_manifest.json').write_text(json.dumps(manifest,indent=2,allow_nan=False),encoding='utf-8')
    require(all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in hashes.items()),'Input hash changed during analysis')
    return manifest


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('conditions',nargs='?',help='Experiment conditions JSON; empty trials creates templates')
    parser.add_argument('--inspect-session',help='Validate a recording and export event/settings inventory without inferring fingers')
    parser.add_argument('--output',required=True,help='New directory, separate from source data')
    args=parser.parse_args()
    try:
        if args.inspect_session:
            require(not args.conditions,'Choose conditions analysis or session inspection')
            data=load_session(args.inspect_session)
            out=Path(args.output).resolve()
            path=data['path']
            require(not out.exists() and out!=path and path not in out.parents and out not in path.parents,'Inspection output must be new and separate')
            out.mkdir(parents=True)
            inventory=dict(session_id=data['meta']['session_id'],session_dir=str(path),
                           callback_range=[min(r['timestamp'] for r in data['callbacks'].values()),max(r['timestamp'] for r in data['callbacks'].values())],
                           settings=list(data['settings'].values()),
                           assignments_to_verify_from_video=[dict(source=e['source'],generation=int(e['connection_generation']),onset_event_index=int(e['event_index']),
                                                                 note=int(e['note']),hand=None,finger=None,observed_sec=finite(e['quest_observed_timestamp_sec']))
                               for e in data['events'] if e['event_type']=='NoteOn' and int(e['velocity'])>0 and not truth(e['synthetic'])])
            (out/'session_inventory.json').write_text(json.dumps(inventory,indent=2),encoding='utf-8')
            print('Recording inventory exported; actual fingers require fixed-camera verification.')
            return
        require(args.conditions is not None,'Provide conditions JSON or --inspect-session')
        result=analyze(args.conditions,args.output)
    except (ValueError,KeyError,OSError) as e:
        parser.exit(2,'Analysis rejected: '+str(e)+'\n')
    print(json.dumps(dict(measured=result['measured'],frames=result['frames'],valid_frames=result['valid_frames'],output=str(Path(args.output).resolve())),indent=2))


if __name__=='__main__':
    main()
