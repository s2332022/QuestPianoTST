"""Reproducible analysis of immutable Quest raw logs (Python standard library only).
No correction and no external ground truth. All distances are KeyboardRoot local units.
"""
import argparse
import bisect
import csv
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path

TIPS = ('ThumbTip', 'IndexTip', 'MiddleTip', 'RingTip', 'LittleTip')
CHAINS = [('Wrist', 'Palm')]
for finger in ('Thumb', 'Index', 'Middle', 'Ring', 'Little'):
    chain = ([finger+'Metacarpal', finger+'Proximal', finger+'Distal', finger+'Tip'] if finger == 'Thumb'
             else [finger+'Metacarpal', finger+'Proximal', finger+'Intermediate', finger+'Distal', finger+'Tip'])
    CHAINS.extend(zip(chain, chain[1:]))


def truth(v):
    return str(v).lower() == 'true'


def xyz(v):
    return tuple(float(v[k]) for k in 'xyz')


def point(m, p):
    return tuple(sum(m[r*4+c]*p[c] for c in range(3))+m[r*4+3] for r in range(3))


def inverse(m):
    rows = [list(m[r*4:r*4+4])+[float(r == c) for c in range(4)] for r in range(4)]
    for c in range(4):
        pivot = max(range(c, 4), key=lambda r: abs(rows[r][c]))
        if abs(rows[pivot][c]) < 1e-12:
            raise ValueError('Singular transform; coordinate reconstruction is impossible')
        rows[c], rows[pivot] = rows[pivot], rows[c]
        divisor = rows[c][c]
        rows[c] = [v/divisor for v in rows[c]]
        for r in range(4):
            if r != c:
                divisor = rows[r][c]
                rows[r] = [a-divisor*b for a, b in zip(rows[r], rows[c])]
    return [v for row in rows for v in row[4:]]


def rotate(q, p):
    # Normalize recorded quaternion; offsets are Tracking-space metres before transform.
    x, y, z, w = q
    n = math.sqrt(sum(v*v for v in q))
    if n < 1e-12:
        raise ValueError('Invalid quaternion for nonzero pad offset')
    x, y, z, w = (v/n for v in (x, y, z, w))
    u = (x, y, z)
    cross = (y*p[2]-z*p[1], z*p[0]-x*p[2], x*p[1]-y*p[0])
    dot = sum(a*b for a, b in zip(u, p))
    return tuple(2*dot*u[i]+(w*w-sum(v*v for v in u))*p[i]+2*w*cross[i] for i in range(3))


def write_csv(path, fields, rows):
    with path.open('w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=fields, extrasaction='ignore')
        w.writeheader()
        w.writerows(rows)


def read_csv(path):
    with path.open(newline='', encoding='utf-8-sig') as f:
        return list(csv.DictReader(f))


def stats(values):
    return (statistics.mean(values), statistics.pstdev(values)) if values else ('', '')


def geometry(p, key):
    lo, hi = xyz(key['min']), xyz(key['max'])
    surface = p[1]-hi[1]
    lateral = max(lo[0]-p[0], p[0]-hi[0], 0.)
    depth = max(lo[2]-p[2], p[2]-hi[2], 0.)
    inside = all(lo[i] <= p[i] <= hi[i] for i in range(3))
    # Penetration only inside the finite key volume, not infinitely below its top.
    return surface, hi[1]-p[1] if inside else 0., lateral, depth


def derive(raw, snapshots, pad_offset=(0., 0., 0.)):
    rows = []
    inverses = {k: inverse(s['keyboard_root']['local_to_world']) for k, s in snapshots.items()}
    previous = {}
    last_frame_time = None
    last_callback = None
    frame_times = {}
    for r in raw:
        t = float(r['quest_timestamp_sec'])
        if not math.isfinite(t):
            raise ValueError('Non-finite hand timestamp')
        callback = int(r['callback_index'])
        if callback != last_callback:
            if last_frame_time is not None and t < last_frame_time:
                raise ValueError('Hand clock moved backwards')
            last_callback, last_frame_time = callback, t
        if callback in frame_times and frame_times[callback] != t:
            raise ValueError('Joints in a callback do not share the frame timestamp')
        frame_times[callback] = t
        sid = int(r['snapshot_id'])
        s = snapshots[sid]  # Never silently use identity or a nearest transform.
        p = tuple(float(r['position_'+a]) for a in 'xyz')
        valid = truth(r['pose_valid']) and truth(r['hand_tracked'])
        row = dict(r, timestamp=t, tracking_valid=valid)
        row.update({'raw_'+a: p[i] for i, a in enumerate('xyz')})
        q = tuple(float(r['rotation_'+a]) for a in 'xyzw')
        if valid:
            if not all(map(math.isfinite, p+q)):
                raise ValueError('Non-finite valid joint pose')
            world = point(s['tracking_origin']['local_to_world'], p)
            keyboard = point(inverses[sid], world)
            offset = rotate(q, pad_offset) if any(pad_offset) else (0., 0., 0.)
            pad = point(inverses[sid], point(s['tracking_origin']['local_to_world'], tuple(p[i]+offset[i] for i in range(3))))
            for prefix, value in (('world', world), ('keyboard', keyboard), ('pad_keyboard', pad)):
                row.update({prefix+'_'+a: value[i] for i, a in enumerate('xyz')})
            prev = previous.get((r['hand'], r['joint_id']))
            if prev and prev['tracking_valid'] and prev['snapshot_id'] == r['snapshot_id']:
                dt = t-prev['timestamp']
                if dt > 0:
                    v = [(keyboard[i]-prev['keyboard_'+a])/dt for i, a in enumerate('xyz')]
                    row.update({'velocity_'+a: v[i] for i, a in enumerate('xyz')})
                    row['speed'] = math.sqrt(sum(x*x for x in v))
                    row.update({'pad_velocity_'+a: (pad[i]-prev['pad_keyboard_'+a])/dt for i, a in enumerate('xyz')})
                    if 'speed' in prev:
                        row.update({'acceleration_'+a: (v[i]-prev['velocity_'+a])/dt for i, a in enumerate('xyz')})
        previous[(r['hand'], r['joint_id'])] = row
        rows.append(row)
    return rows


def summarize(rows, static_intervals):
    groups = defaultdict(list)
    for r in rows:
        groups[(r['hand'], r['joint_name'])].append(r)
    summaries = []
    for (hand, joint), group in sorted(groups.items()):
        valid = [r for r in group if r['tracking_valid']]
        result = dict(hand=hand, joint=joint, frames=len(group), valid_frames=len(valid), loss_rate=1-len(valid)/len(group))
        for a in 'xyz':
            result['mean_'+a], result['std_'+a] = stats([r['keyboard_'+a] for r in valid])
            for space in ('raw', 'world'):
                result[space+'_mean_'+a], result[space+'_std_'+a] = stats([r[space+'_'+a] for r in valid])
        speeds = [r['speed'] for r in valid if 'speed' in r]
        result['mean_speed'], result['max_speed'] = (statistics.mean(speeds), max(speeds)) if speeds else ('', '')
        result['velocity_samples'] = len(speeds)
        summaries.append(result)
    jitter = []
    # Static is supplied by the operator: do not label all motion as jitter.
    for index, (start, end) in enumerate(static_intervals):
        for (hand, joint), group in sorted(groups.items()):
            segment = [r for r in group if start <= r['timestamp'] <= end and r['tracking_valid']]
            for sid in sorted({r['snapshot_id'] for r in segment}):
                selected = [r for r in segment if r['snapshot_id'] == sid]
                result = dict(interval=index, start=start, end=end, hand=hand, joint=joint, snapshot_id=sid, valid_frames=len(selected))
                for a in 'xyz':
                    _, result['std_'+a] = stats([r['keyboard_'+a] for r in selected])
                jitter.append(result)
    return summaries, jitter


def bones(rows):
    frames = defaultdict(dict)
    for r in rows:
        if r['tracking_valid']:
            frames[(r['callback_index'], r['hand'])][r['joint_name']] = r
    lengths = defaultdict(list)
    samples = []
    for (_, hand), joints in frames.items():
        for first, second in CHAINS:
            if first not in joints or second not in joints:
                continue
            a, b = joints[first], joints[second]
            # Tracking space isolates anatomical consistency from recentering/calibration.
            length = math.sqrt(sum((a['raw_'+axis]-b['raw_'+axis])**2 for axis in 'xyz'))
            lengths[(hand, first, second)].append(length)
            samples.append(dict(timestamp=a['timestamp'], hand=hand, first_joint=first, second_joint=second, length=length))
    summary = []
    for (hand, a, b), values in sorted(lengths.items()):
        mean, std = stats(values)
        summary.append(dict(hand=hand, first_joint=a, second_joint=b, valid_frames=len(values), mean_length=mean, std_length=std, min_length=min(values), max_length=max(values)))
    return samples, summary


def align(rows, events, snapshots, window):
    candidates = {(hand, finger): [] for hand in ('Left', 'Right') for finger in TIPS}
    for r in rows:
        if r['tracking_valid'] and r['joint_name'] in TIPS:
            candidates[(r['hand'], r['joint_name'])].append(r)
    times = {}
    for identity, group in candidates.items():
        group.sort(key=lambda r: r['timestamp'])
        times[identity] = [r['timestamp'] for r in group]
    alignment, trajectories = [], []
    for event in events:
        if event['event_type'] != 'NoteOn' or int(event['velocity']) == 0 or truth(event['synthetic']):
            continue
        t, note = float(event['quest_receive_timestamp_sec']), int(event['note'])
        sid = int(event['snapshot_id'])
        for (hand, finger), group in sorted(candidates.items()):
            # A transform transition is not hand motion. Align only within the event's snapshot.
            left = bisect.bisect_left(times[(hand, finger)], t-window)
            right = bisect.bisect_right(times[(hand, finger)], t+window)
            nearby = [r for r in group[left:right] if int(r['snapshot_id']) == sid]
            base = dict(source=event['source'], event_index=event['event_index'], midi_timestamp=t, note=note, velocity=event['velocity'], hand=hand, candidate_finger=finger, candidate_status='unassigned', snapshot_id=sid)
            if not nearby:
                alignment.append(dict(base, sample_status='no_valid_hand_in_window'))
                continue
            key = next((k for k in snapshots[sid]['keys'] if k['note'] == note), None)
            if key is None:
                alignment.append(dict(base, sample_status='note_outside_recorded_keyboard'))
                continue
            def measurements(r):
                return geometry(tuple(r['pad_keyboard_'+a] for a in 'xyz'), key)
            # Distance to finite top rectangle, includes lateral/depth miss (avoids distant false matches).
            nearest = min(nearby, key=lambda r: sum(v*v for v in (measurements(r)[0], measurements(r)[2], measurements(r)[3])))
            surface, penetration, lateral, depth = measurements(nearest)
            result = dict(base, sample_status='candidate', nearest_time=nearest['timestamp'], time_offset_ms=(t-nearest['timestamp'])*1000,
                          surface_distance=surface, penetration_depth=penetration, lateral_overhang=lateral, depth_overhang=depth,
                          normal_velocity=nearest.get('pad_velocity_y', ''))
            result.update({'keyboard_'+a: nearest['pad_keyboard_'+a] for a in 'xyz'})
            alignment.append(result)
            for r in nearby:
                trajectory = dict(base, timestamp=r['timestamp'], relative_time_ms=(r['timestamp']-t)*1000, surface_distance=measurements(r)[0], normal_velocity=r.get('pad_velocity_y', ''))
                trajectory.update({'keyboard_'+a: r['pad_keyboard_'+a] for a in 'xyz'})
                trajectories.append(trajectory)
    return alignment, trajectories


def nonpressed(rows, events, snapshots):
    # Physical MIDI key-down (CC64 sustain does not mean a key is physically down).
    # State starts unknown: absence of an event is not proof that a key was released.
    events = sorted(events, key=lambda e: float(e['quest_receive_timestamp_sec']))
    active, known, generations = {}, set(), {}
    cursor = 0
    output = []
    for row in sorted(rows, key=lambda r: r['timestamp']):
        while cursor < len(events) and float(events[cursor]['quest_receive_timestamp_sec']) <= row['timestamp']:
            e = events[cursor];cursor += 1
            source, channel, note = e['source'], int(e['channel']), int(e['note'])
            generation = e.get('connection_generation', '')
            reset = truth(e['synthetic']) or (source == 'BLE' and generation != generations.get(source, generation))
            generations[source] = generation
            if reset:
                for owner in list(known):
                    if owner[0] == source:
                        known.remove(owner)
                        active.pop(owner, None)
            # Synthetic disconnect releases protect visuals; they do not observe physical key state.
            if truth(e['synthetic']):
                continue
            if e['event_type'] in ('NoteOn', 'NoteOff'):
                key = source, channel, note
                known.add(key);active[key] = e['event_type'] == 'NoteOn' and int(e['velocity']) > 0
            elif e['event_type'] == 'ControlChange' and int(e['control']) in (120, 123):
                for n in range(128):
                    known.add((source, channel, n));active[source, channel, n] = False
        if not row['tracking_valid'] or row['joint_name'] not in TIPS:
            continue
        for key in snapshots[int(row['snapshot_id'])]['keys']:
            surface, penetration, _, _ = geometry(tuple(row['pad_keyboard_'+a] for a in 'xyz'), key)
            if penetration <= 0:
                continue
            owners = [owner for owner in known if owner[2] == key['note']]
            state = 'pressed' if any(active[o] for o in owners) else 'observed_released' if owners else 'unknown'
            output.append(dict(timestamp=row['timestamp'], hand=row['hand'], joint=row['joint_name'], note=key['note'], midi_state=state,
                               penetration_depth=penetration, surface_distance=surface, snapshot_id=row['snapshot_id']))
    return output


DERIVED = ['session_id','timestamp','quest_timestamp_sec','unity_frame','callback_index','update_type','hand','hand_tracked','joint_id','joint_name','tracking_valid','pose_valid','tracking_state','snapshot_id','success_flags']
DERIVED += [p+'_'+a for p in ('raw','world','keyboard','pad_keyboard','velocity','pad_velocity','acceleration') for a in 'xyz']
DERIVED += ['rotation_'+a for a in 'xyzw']+['speed']
JOINT = ['hand','joint','frames','valid_frames','loss_rate']+[p+'_'+a for p in ('mean','std') for a in 'xyz']+['mean_speed','max_speed','velocity_samples']+[s+'_'+p+'_'+a for s in ('raw','world') for p in ('mean','std') for a in 'xyz']
ALIGN = ['source','event_index','midi_timestamp','note','velocity','hand','candidate_finger','candidate_status','sample_status','snapshot_id','nearest_time','time_offset_ms','keyboard_x','keyboard_y','keyboard_z','surface_distance','penetration_depth','lateral_overhang','depth_overhang','normal_velocity']


def analyze(directory, output, static_intervals=(), pad_offset=(0., 0., 0.), window=.15):
    directory, output = Path(directory).resolve(), Path(output).resolve()
    if output == directory or directory in output.parents:
        raise ValueError('Use a separate analysis output directory to preserve source session')
    metadata = json.loads((directory/'session_metadata.json').read_text(encoding='utf-8-sig'))
    status = json.loads((directory/'research_recording_summary.json').read_text(encoding='utf-8-sig'))
    if not status.get('writer_closed') or status.get('queue_overflows') or status.get('error'):
        raise ValueError('Recording incomplete; inspect research_recording_summary.json')
    snapshots = {}
    for line in (directory/'transform_snapshots.jsonl').read_text(encoding='utf-8-sig').splitlines():
        s = json.loads(line)
        if s['snapshot_id'] in snapshots:
            raise ValueError('Duplicate transform snapshot id')
        snapshots[s['snapshot_id']] = s
    raw = read_csv(directory/'quest_hand_raw.csv')
    events = read_csv(directory/'quest_midi_research.csv')
    if any(r['session_id'] != metadata['session_id'] for r in raw+events):
        raise ValueError('Mixed session ids')
    rows = derive(raw, snapshots, pad_offset)
    summary, jitter = summarize(rows, static_intervals)
    bone_samples, bone_summary = bones(rows)
    alignment, trajectories = align(rows, events, snapshots, window)
    output.mkdir(parents=True, exist_ok=True)
    write_csv(output/'derived_hand_coordinates.csv', DERIVED, rows)
    write_csv(output/'joint_summary.csv', JOINT, summary)
    write_csv(output/'static_jitter.csv', ['interval','start','end','hand','joint','snapshot_id','valid_frames','std_x','std_y','std_z'], jitter)
    write_csv(output/'bone_lengths.csv', ['timestamp','hand','first_joint','second_joint','length'], bone_samples)
    write_csv(output/'bone_summary.csv', ['hand','first_joint','second_joint','valid_frames','mean_length','std_length','min_length','max_length'], bone_summary)
    write_csv(output/'midi_hand_alignment.csv', ALIGN, alignment)
    write_csv(output/'midi_hand_trajectories.csv', ['source','event_index','midi_timestamp','note','hand','candidate_finger','timestamp','relative_time_ms','keyboard_x','keyboard_y','keyboard_z','surface_distance','normal_velocity','snapshot_id'], trajectories)
    write_csv(output/'key_penetration.csv', ['timestamp','hand','joint','note','midi_state','penetration_depth','surface_distance','snapshot_id'], nonpressed(rows, events, snapshots))
    result = dict(session_id=metadata['session_id'], hand_rows=len(rows), valid_rows=sum(r['tracking_valid'] for r in rows), midi_events=len(events), snapshots=len(snapshots),
                  duration_sec=max((r['timestamp'] for r in rows), default=0)-min((r['timestamp'] for r in rows), default=0), external_ground_truth=False,
                  alignment_window_sec=window, pad_offset_x=pad_offset[0], pad_offset_y=pad_offset[1], pad_offset_z=pad_offset[2])
    write_csv(output/'session_summary.csv', list(result), [result])
    import hashlib
    manifest = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in directory.iterdir() if p.is_file() and p.suffix in ('.csv','.json','.jsonl')}
    (output/'analysis_manifest.json').write_text(json.dumps(dict(analysis_tool_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), source_sha256=manifest, static_intervals=static_intervals, pad_offset=pad_offset, alignment_window_sec=window,
        interpretation='Repeatability, keyboard geometric consistency, MIDI timing consistency and anatomical consistency; no ground-truth position accuracy',
        geometry='Rest key bounding boxes, no animated depression, no fingertip contact assumption; white/black overlap is not resolved',
        nonpressed='observed_released only establishes MIDI released state for observed source/channel; unobserved sources remain unknown; synthetic disconnect and BLE generation transitions invalidate source state',
        velocity='Finite differences of consecutive valid observations in the same transform snapshot; no differentiation across invalid frames or transform changes'), indent=2), encoding='utf-8')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('session')
    parser.add_argument('--output', required=True)
    parser.add_argument('--static', nargs=2, type=float, action='append', default=[], metavar=('START','END'), help='Operator-labelled static interval in Quest absolute seconds; repeatable')
    parser.add_argument('--pad-offset', nargs=3, type=float, default=(0,0,0), metavar=('X','Y','Z'))
    parser.add_argument('--window', type=float, default=.15)
    args = parser.parse_args()
    if args.window <= 0 or not math.isfinite(args.window) or any(a >= b or not all(map(math.isfinite,(a,b))) for a,b in args.static) or not all(map(math.isfinite,args.pad_offset)):
        parser.error('Window must be positive and intervals increasing; all parameters must be finite')
    print(json.dumps(analyze(args.session,args.output,args.static,args.pad_offset,args.window), indent=2))


if __name__ == '__main__':
    main()
