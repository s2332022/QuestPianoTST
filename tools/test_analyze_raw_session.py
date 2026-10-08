import csv
import importlib.util
import json
import math
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('analysis', Path(__file__).with_name('analyze_raw_session.py'))
a = importlib.util.module_from_spec(spec)
spec.loader.exec_module(a)
I = [1.,0,0,0, 0,1.,0,0, 0,0,1.,0, 0,0,0,1.]


def snapshot(origin=None, keyboard=None, sid=0):
    return dict(snapshot_id=sid, quest_timestamp_sec=0, tracking_origin=dict(local_to_world=origin or I),
                keyboard_root=dict(local_to_world=keyboard or I), keys=[dict(note=60,min=dict(x=-1,y=-1,z=-1),max=dict(x=1,y=0,z=1))])


def row(t, p, name='IndexTip', valid=True, callback=None, sid=0):
    r = dict(session_id='test',quest_timestamp_sec=str(t),callback_index=str(t if callback is None else callback),unity_frame='1',update_type='Dynamic',hand='Left',hand_tracked='True',joint_id={'IndexTip':'10','Wrist':'1','Palm':'2'}.get(name,'3'),joint_name=name,pose_valid=str(valid),tracking_state='1',snapshot_id=str(sid))
    r.update({'position_'+axis:str(p[i]) for i,axis in enumerate('xyz')})
    r.update({'rotation_'+axis:str(v) for axis,v in zip('xyzw',(0,0,0,1))})
    return r


def event(t=1, note=60, type='NoteOn', source='BLE'):
    return dict(session_id='test',source=source,quest_receive_timestamp_sec=str(t),sender_timestamp_sec='1234.5',ble_timestamp_13bit_ms='8191',connection_generation='7',event_index='1',event_type=type,channel='1',note=str(note),velocity='100' if type=='NoteOn' else '0',control='-1',value='-1',synthetic='False',snapshot_id='0')


class AnalysisTests(unittest.TestCase):
    def test_identity_translation_rotation_scale_and_inverse(self):
        cases = [(I,(1,2,3)), ([1,0,0,4,0,1,0,5,0,0,1,6,0,0,0,1],(5,7,9)),
                 ([0,0,1,0,0,1,0,0,-1,0,0,0,0,0,0,1],(3,2,-1)),
                 ([2,0,0,0,0,3,0,0,0,0,4,0,0,0,0,1],(2,6,12))]
        for m, expected in cases:
            self.assertEqual(a.point(m,(1,2,3)),expected)
            for x,y in zip(a.point(a.inverse(m),expected),(1,2,3)):
                self.assertAlmostEqual(x,y)
        m = cases[1][0]
        d=a.derive([row(0,(1,2,3))],{0:snapshot(m,m)})[0]
        self.assertEqual(d['world_x'],5)
        self.assertEqual(d['keyboard_x'],1)
        with self.assertRaises(ValueError): a.inverse([0]*16)

    def test_validity_statistics_velocity_loss_and_raw_preservation(self):
        raw=[row(0,(0,0,0)),row(1,(2,0,0)),row(2,(99,0,0),valid=False),row(3,(4,0,0))]
        original=json.dumps(raw)
        derived=a.derive(raw,{0:snapshot()})
        summary,_=a.summarize(derived,[])
        s=summary[0]
        self.assertEqual(s['valid_frames'],3)
        self.assertEqual(s['loss_rate'],.25)
        self.assertAlmostEqual(s['std_x'],math.sqrt(8/3))
        self.assertEqual(derived[1]['velocity_x'],2)
        self.assertNotIn('speed',derived[3])
        self.assertEqual(json.dumps(raw),original)
        self.assertEqual(derived[2]['raw_x'],99)
        self.assertNotIn('world_x',derived[2])

    def test_monotonicity_and_shared_joint_timestamp(self):
        with self.assertRaises(ValueError):a.derive([row(2,(0,0,0)),row(1,(0,0,0))],{0:snapshot()})
        with self.assertRaises(ValueError):a.derive([row(1,(0,0,0),callback=1),row(2,(0,0,0),name='Wrist',callback=1)],{0:snapshot()})
        self.assertEqual(len(a.derive([row(1,(0,0,0),callback=1),row(1,(0,0,0),name='Wrist',callback=1)],{0:snapshot()})),2)

    def test_transform_transition_breaks_velocity_and_missing_snapshot_fails(self):
        d=a.derive([row(0,(0,0,0)),row(1,(1,0,0),sid=1)],{0:snapshot(),1:snapshot(sid=1)})
        self.assertNotIn('speed',d[1])
        with self.assertRaises(KeyError):a.derive([row(0,(0,0,0),sid=9)],{0:snapshot()})

    def test_static_jitter_bone_length_and_pad_model(self):
        raw=[]
        for t in (0,1,2):
            raw.extend([row(t,(t,0,0),name='Wrist'),row(t,(t+1,0,0),name='Palm')])
        d=a.derive(raw,{0:snapshot()})
        samples,summary=a.bones(d)
        self.assertEqual(len(samples),3)
        self.assertEqual(summary[0]['mean_length'],1)
        self.assertEqual(summary[0]['std_length'],0)
        _,jitter=a.summarize(d,[(0,1)])
        self.assertEqual(jitter[0]['std_x'],.5)
        q=(0,0,math.sqrt(.5),math.sqrt(.5))
        rotated=a.rotate(q,(1,0,0))
        self.assertAlmostEqual(rotated[1],1)
        d=a.derive([row(0,(0,0,0))],{0:snapshot()},(0,-.01,0))
        self.assertEqual(d[0]['keyboard_y'],0)
        self.assertEqual(d[0]['pad_keyboard_y'],-.01)

    def test_alignment_is_candidate_and_uses_quest_receive_time(self):
        d=a.derive([row(0,(0,.3,0)),row(1,(0,-.1,0)),row(2,(0,.4,0))],{0:snapshot()})
        aligned,trajectory=a.align(d,[event(1.1)],{0:snapshot()},.2)
        self.assertEqual(aligned[0]['candidate_status'],'unassigned')
        self.assertEqual(aligned[0]['nearest_time'],1)
        self.assertAlmostEqual(aligned[0]['time_offset_ms'],100)
        self.assertAlmostEqual(aligned[0]['penetration_depth'],.1)
        self.assertEqual(len(trajectory),1)
        self.assertEqual(a.align(d,[dict(event(),synthetic='True')],{0:snapshot()},.2)[0],[])
        self.assertEqual(a.align(d,[dict(event(),velocity='0')],{0:snapshot()},.2)[0],[])
        self.assertEqual(a.align(d,[event(9)],{0:snapshot()},.2)[0][0]['sample_status'],'no_valid_hand_in_window')

    def test_finite_key_geometry_and_unknown_nonpressed_state(self):
        key=snapshot()['keys'][0]
        self.assertEqual(a.geometry((0,-2,0),key)[1],0)
        self.assertEqual(a.geometry((2,-.5,0),key)[1],0)
        d=a.derive([row(1,(0,-.5,0))],{0:snapshot()})
        self.assertEqual(a.nonpressed(d,[],{0:snapshot()})[0]['midi_state'],'unknown')
        self.assertEqual(a.nonpressed(d,[event(.5,type='NoteOff')],{0:snapshot()})[0]['midi_state'],'observed_released')
        self.assertEqual(a.nonpressed(d,[event(.5)],{0:snapshot()})[0]['midi_state'],'pressed')

    def test_invalid_values_all_invalid_candidates_and_synthetic_release(self):
        with self.assertRaises(ValueError):a.derive([row(float('nan'),(0,0,0))],{0:snapshot()})
        with self.assertRaises(ValueError):a.derive([row(0,(float('inf'),0,0))],{0:snapshot()})
        d=a.derive([row(1,(0,-.5,0),valid=False)],{0:snapshot()})
        aligned,_=a.align(d,[event()],{0:snapshot()},.2)
        self.assertEqual(len(aligned),10)
        self.assertTrue(all(r['sample_status']=='no_valid_hand_in_window' for r in aligned))
        d=a.derive([row(1,(0,-.5,0))],{0:snapshot()})
        events=[event(.2),dict(event(.5,type='NoteOff'),synthetic='True')]
        self.assertEqual(a.nonpressed(d,events,{0:snapshot()})[0]['midi_state'],'unknown')
        events=[event(.2,note=60,type='NoteOff'),dict(event(.5,note=62),connection_generation='8')]
        self.assertEqual(a.nonpressed(d,events,{0:snapshot()})[0]['midi_state'],'unknown')

    def test_end_to_end_outputs_and_source_hashes(self):
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp);session=base/'session';session.mkdir();output=base/'analysis'
            (session/'session_metadata.json').write_text(json.dumps(dict(session_id='test')))
            (session/'research_recording_summary.json').write_text(json.dumps(dict(writer_closed=True,queue_overflows=0,error=None)))
            (session/'transform_snapshots.jsonl').write_text(json.dumps(snapshot())+'\n')
            raw=[row(0,(0,0,0)),row(1,(0,-.1,0))]
            a.write_csv(session/'quest_hand_raw.csv',list(raw[0]),raw)
            a.write_csv(session/'quest_midi_research.csv',list(event()),[event()])
            before={p.name:p.read_bytes() for p in session.iterdir()}
            result=a.analyze(session,output)
            self.assertEqual(result['valid_rows'],2)
            for name in ('session_summary.csv','joint_summary.csv','midi_hand_alignment.csv','derived_hand_coordinates.csv','bone_summary.csv','static_jitter.csv','key_penetration.csv'):
                self.assertTrue((output/name).exists())
            self.assertEqual({p.name:p.read_bytes() for p in session.iterdir()},before)
            self.assertEqual(a.read_csv(session/'quest_midi_research.csv')[0]['ble_timestamp_13bit_ms'],'8191')
            with self.assertRaises(ValueError):a.analyze(session,session/'derived')
            (session/'research_recording_summary.json').write_text(json.dumps(dict(writer_closed=False)))
            with self.assertRaises(ValueError):a.analyze(session,output)


if __name__=='__main__':unittest.main()
