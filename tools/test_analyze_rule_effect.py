"""SYNTHETIC TEST DATA ONLY. Never evidence of measured correction efficacy."""
import copy
import json
import tempfile
import subprocess
import sys
import unittest
from pathlib import Path

import analyze_rule_effect as a
from analyze_raw_session import write_csv

IDENTITY = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]


def fixture(root):
    session=root/'synthetic_session'
    session.mkdir()
    def js(name,value):
        (session/name).write_text(json.dumps(value),encoding='utf-8')
    js('session_metadata.json',dict(session_id='SYNTHETIC_ONLY'))
    js('research_recording_summary.json',dict(writer_closed=True,queue_overflows=0,error=''))
    key=dict(note=60,min=dict(x=0,y=-.02,z=0),max=dict(x=.02,y=0,z=.1))
    snap=dict(snapshot_id=0,quest_timestamp_sec=0,keys=[key],tracking_origin=dict(local_to_world=IDENTITY),keyboard_root=dict(local_to_world=IDENTITY))
    (session/'transform_snapshots.jsonl').write_text(json.dumps(snap)+'\n',encoding='utf-8')
    common=dict(leftHand=False,tipToContactKeyboardMeters=dict(x=0,y=0,z=0),belowSurfaceToleranceMeters=.002,aboveSurfaceToleranceMeters=.003)
    mapping=[-1]*128
    mapping[60]=1
    rule=dict(contact=True,joints=True,nonpenetration=True,interference=True,manualNoteMap=True,noteToFinger=mapping,
              fingers=[dict(contactOffsetKeyboardMeters=dict(x=0,y=0,z=0),maximumBendDegrees=dict(x=90,y=110,z=90)) for _ in range(5)])
    settings=dict(revision=1,quest_timestamp_sec=0,configuration_valid=True,common_settings_json=json.dumps(common),rule_settings_json=json.dumps(rule))
    (session/'rule_pose_settings.jsonl').write_text(json.dumps(settings)+'\n',encoding='utf-8')
    raw,rules,joints=[],[],[]
    for cb,t in enumerate((1.,1.1,1.2,1.3,1.4,1.5,1.6)):
        for j,name in enumerate(('IndexProximal','IndexIntermediate','IndexDistal','IndexTip','Wrist','Palm')):
            p=dict(x=.03,y=.01,z=.02+j*.01)
            r=dict(session_id='SYNTHETIC_ONLY',callback_index=cb,quest_timestamp_sec=t,snapshot_id=0,hand='Right',
                   joint_name=name,joint_id=j,hand_tracked=True,pose_valid=True,update_type='Dynamic',tracking_state=3,
                   rotation_x=0,rotation_y=0,rotation_z=0,rotation_w=1,**{'position_'+k:v for k,v in p.items()})
            raw.append(r)
            pair=dict(session_id='SYNTHETIC_ONLY',callback_index=cb,quest_timestamp_sec=t,snapshot_id=0,hand='Right',joint_name=name,joint_id=j,
                      requested_method='FullRules',executed_method='FullRules',coordinate_space='KeyboardRootLocal',position_unit='m',settings_revision=1,pose_valid=True,
                      **{'raw_'+k:v for k,v in p.items()},corrected_x=.015,corrected_y=.001,corrected_z=p['z'])
            joints.append(pair)
        p=raw[-3]  # IndexTip
        contact=dict(joints[-3],finger='Index',coordinates_valid=True,calibration_id='cal1',note=60,source='BLE',generation=1,channel=0,
                     onset_event_index=10,event_index=10,state='Holding',reason='',contact_reason='',enabled=True,solver_finger_valid=True,gain=1,
                     target_capped=False,solver_limited=False,processing_ms=.5,
                     interference_before_m=0,interference_after_m=0,penetration_before_m=0,penetration_after_m=0)
        if t < 1.05:
            contact.update(note=-1,source='',onset_event_index=-1,event_index=-1,state='Inactive',gain=0)
        elif t >= 1.45:
            contact.update(event_index=11,state='Releasing')
        rules.append(contact)
    event=dict(session_id='SYNTHETIC_ONLY',source='BLE',connection_generation=1,event_index=10,event_type='NoteOn',channel=0,note=60,
               velocity=90,control=0,value=0,synthetic=False,snapshot_id=0,quest_receive_timestamp_sec=1.04,quest_observed_timestamp_sec=1.05)
    off=dict(event,event_index=11,event_type='NoteOff',velocity=0,quest_receive_timestamp_sec=1.44,quest_observed_timestamp_sec=1.45)
    for name,rows in (('quest_hand_raw.csv',raw),('quest_rule_pose.csv',rules),('quest_rule_pose_joints.csv',joints),('quest_midi_research.csv',[event,off])):
        write_csv(session/name,list(rows[0]),rows)
    trial=dict(participant='SYNTHETIC_P',trial_id='SYNTHETIC_T',task='white_single',session_dir='synthetic_session',session_id='SYNTHETIC_ONLY',
               start_sec=1.,end_sec=1.6,settings_revision=1,calibration_id='cal1',video_verified=True,calibration_verified=True,
               assignments=[dict(source='BLE',generation=1,onset_event_index=10,note=60,hand='Right',finger='Index')],static_intervals=[[1.1,1.4]])
    config=root/'synthetic_conditions.json'
    config.write_text(json.dumps(dict(schema_version=1,trials=[trial])),encoding='utf-8')
    return session,config,trial


class GeometryTests(unittest.TestCase):
    def test_union_not_center_and_tie(self):
        geometry=(0,[(0,0,1,1),(2,0,3,1)])
        self.assertEqual(a.errors((.8,.2,.9),geometry,.1,.1)['x'],0)
        self.assertEqual(a.errors((1.5,0,.5),geometry,0,0)['x'],.5)
        self.assertEqual(a.errors((1.5,0,.5),geometry,0,0),a.errors((1.5,0,.5),(0,list(reversed(geometry[1]))),0,0))

    def test_signed_height_band(self):
        geometry=(.01,[(0,0,1,1)])
        v=a.errors((.5,-.01,.5),geometry,.005,.005)
        self.assertAlmostEqual(v['signed_y'],-.02)
        self.assertAlmostEqual(v['penetration'],.02)
        self.assertAlmostEqual(v['band'],.015)
        self.assertEqual(v['float'],0)

    def test_white_black_cutout(self):
        def key(n,x0,x1,z0,z1,y):
            return dict(note=n,min=dict(x=x0,y=-1,z=z0),max=dict(x=x1,y=y,z=z1))
        snap=dict(keys=[key(60,0,1,0,2,0),key(61,.7,1.3,1,2,.2)])
        white=a.surface(snap,60)
        self.assertEqual(a.errors((.8,0,.5),white,0,0)['x'],0)
        self.assertGreater(a.errors((.8,0,1.5),white,0,0)['x'],0)
        self.assertEqual(a.errors((1.,.2,1.5),a.surface(snap,61),0,0)['x'],0)
        self.assertEqual(a.surface(snap,61)[0],.2)

    def test_rate_zero_and_worsening(self):
        self.assertEqual(a.improvement(0,.001),(-.001,None))
        self.assertEqual(a.improvement(.01,.005),(.005,50))

    def test_bootstrap_reproducible_hierarchy(self):
        rows=[dict(participant='a',value=1),dict(participant='a',value=3),dict(participant='b',value=8)]
        self.assertEqual(a.bootstrap(rows,'value'),a.bootstrap(rows,'value'))
        lo,hi=a.bootstrap(rows,'value')
        self.assertLessEqual(lo,4.5)
        self.assertGreaterEqual(hi,4.5)
        self.assertEqual(a.bootstrap(rows[:1],'value'),(None,None))


class IntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)
        self.session,self.config,self.trial=fixture(self.root)

    def tearDown(self):
        self.temp.cleanup()

    def data(self):
        return a.load_session(self.session)

    def test_full_pipeline_input_immutable_and_figures(self):
        before={p.name:p.read_bytes() for p in self.session.iterdir()}
        m=a.analyze(self.config,self.root/'out')
        self.assertEqual(m['frames'],4)
        rows=a.read_csv(self.root/'out'/'paired_frames.csv')
        self.assertAlmostEqual(float(rows[0]['delta_x_m']),.01)
        self.assertAlmostEqual(float(rows[0]['delta_y_m']),.009)
        self.assertEqual(rows[0]['rate_z_pct'],'')
        self.assertTrue(all(r['applicable']=='True' for r in rows))
        self.assertEqual(before,{p.name:p.read_bytes() for p in self.session.iterdir()})
        import xml.etree.ElementTree as ET
        for p in (self.root/'out').glob('*.svg'):
            ET.parse(p)
        self.assertGreater(len(list((self.root/'out').glob('*.svg'))),10)
        self.assertTrue(any(r['metric']=='static_jitter' for r in a.read_csv(self.root/'out'/'side_effects.csv')))

    def test_missing_callback_rejected(self):
        d=self.data()
        del d['joints'][(1,'Right','IndexTip')]
        with self.assertRaisesRegex(ValueError,'Missing paired'):
            a.trial_frames(d,self.trial)

    def test_snapshot_mismatch_rejected(self):
        d=self.data()
        d['joints'][(1,'Right','IndexTip')]['snapshot_id']='99'
        with self.assertRaisesRegex(ValueError,'Snapshot mismatch'):
            a.trial_frames(d,self.trial)

    def test_transform_reconstruction_and_changes(self):
        d=self.data()
        raw=list(d['raw'].values())[0]
        rows=a.read_csv(self.session/'quest_hand_raw.csv')
        snap=copy.deepcopy(d['snapshots'][0])
        snap['tracking_origin']['local_to_world']=IDENTITY.copy()
        snap['tracking_origin']['local_to_world'][3]=.1
        r=a.derive(rows,{0:snap})
        self.assertAlmostEqual(r[0]['keyboard_x'],raw['keyboard_x']+.1)
        d['joints'][(1,'Right','IndexTip')]['raw_x']='.13'
        with self.assertRaisesRegex(ValueError,'reconstruction'):
            a.trial_frames(d,self.trial)

    def test_invalid_tracking_retained(self):
        d=self.data()
        d['raw'][(1,'Right','IndexTip')]['tracking_valid']=False
        rows,_=a.trial_frames(d,self.trial)
        self.assertEqual(len(rows),4)
        self.assertEqual(sum(r['valid'] for r in rows),3)
        self.assertFalse(rows[0]['applicable'])
        self.assertNotIn('delta_x_m',rows[0])

    def test_wrong_time_unit_revision_method_calibration(self):
        for field,value in [('quest_timestamp_sec','99'),('position_unit','mm'),('settings_revision','2'),('executed_method','Legacy')]:
            with self.subTest(field=field):
                d=self.data()
                d['joints'][(1,'Right','IndexTip')][field]=value
                with self.assertRaises(ValueError):
                    a.trial_frames(d,self.trial)
        t=dict(self.trial,calibration_id='wrong')
        with self.assertRaisesRegex(ValueError,'Calibration'):
            a.trial_frames(self.data(),t)

    def test_unassigned_and_conflict_not_success(self):
        for field,value in [('note','-1'),('onset_event_index','-1'),('gain','0'),('solver_finger_valid','False')]:
            with self.subTest(field=field):
                d=self.data()
                for contact in d['rules'].values():
                    contact[field]=value
                rows,_=a.trial_frames(d,self.trial)
                self.assertTrue(all(not r['applicable'] for r in rows))
                self.assertTrue(all('delta_x_m' in r for r in rows))

    def test_manual_map_actual_finger_distinct(self):
        d=self.data()
        d['settings'][1]['rule']['noteToFinger'][60]=-1
        rows,_=a.trial_frames(d,self.trial)
        self.assertTrue(all(r['failure']=='manual_map_mismatch' for r in rows))
        t=dict(self.trial,assignments=[])
        with self.assertRaisesRegex(ValueError,'fingering'):
            a.trial_frames(d,t)

    def test_noteoff_and_receive_observe(self):
        d=self.data()
        rows,tracks=a.trial_frames(d,self.trial)
        self.assertEqual([round(r['timestamp'],1) for r in rows],[1.1,1.2,1.3,1.4])
        self.assertAlmostEqual(rows[0]['relative_receive_ms']-rows[0]['relative_observed_ms'],10)
        self.assertGreater(len(tracks),len(rows))
        d['events'][1]['event_type']='NoteOn'
        self.assertEqual(a.active_events(d,1,1.6)[0][2],1.45) # velocity-zero off

    def test_generation_reset(self):
        d=self.data()
        d['events'][1]['connection_generation']='2'
        spans=a.active_events(d,1,1.6)
        self.assertEqual(spans[0][2],1.45)

    def test_chord_intervals(self):
        d=self.data()
        event=dict(d['events'][0],note='64',event_index='12')
        d['events'].insert(1,event)
        spans=a.active_events(d,1,1.6)
        self.assertEqual(len(spans),2)
        self.assertEqual({int(e['note']) for e,_,_ in spans},{60,64})

    def test_chord_full_paired_pipeline(self):
        # Two simultaneous notes, independently video-confirmed fingers.
        for filename in ('quest_hand_raw.csv','quest_rule_pose_joints.csv','quest_rule_pose.csv'):
            path=self.session/filename
            rows=a.read_csv(path)
            added=[]
            for r in rows:
                if r.get('finger')=='Index' or r.get('joint_name','').startswith('Index'):
                    r=dict(r)
                    r['joint_name']=r.get('joint_name','IndexTip').replace('Index','Middle')
                    if 'joint_id' in r:
                        r['joint_id']=str(int(r['joint_id'])+10)
                    for field in ('position_x','raw_x','corrected_x'):
                        if field in r:
                            r[field]=str(float(r[field])+.04)
                    if 'finger' in r:
                        r['finger']='Middle'
                        if r['note']!='-1':
                            r.update(note='64',onset_event_index='12',event_index='13' if r['state']=='Releasing' else '12')
                    added.append(r)
            write_csv(path,list(rows[0]),rows+added)
        # Restore callback ordering required by the immutable raw provider.
        path=self.session/'quest_hand_raw.csv'
        rows=sorted(a.read_csv(path),key=lambda r:int(r['callback_index']))
        write_csv(path,list(rows[0]),rows)
        path=self.session/'quest_midi_research.csv'
        events=a.read_csv(path)
        events += [dict(events[0],event_index='12',note='64'),dict(events[1],event_index='13',note='64')]
        write_csv(path,list(events[0]),events)
        path=self.session/'transform_snapshots.jsonl'
        snap=json.loads(path.read_text())
        key=copy.deepcopy(snap['keys'][0])
        key['note']=64
        key['min']['x']+=.04
        key['max']['x']+=.04
        snap['keys'].append(key)
        path.write_text(json.dumps(snap)+'\n')
        path=self.session/'rule_pose_settings.jsonl'
        settings=json.loads(path.read_text())
        rule=json.loads(settings['rule_settings_json'])
        rule['noteToFinger'][64]=2
        settings['rule_settings_json']=json.dumps(rule)
        path.write_text(json.dumps(settings)+'\n')
        trial=copy.deepcopy(self.trial)
        trial['task']='chord'
        trial['assignments'].append(dict(source='BLE',generation=1,onset_event_index=12,note=64,hand='Right',finger='Middle'))
        rows,_=a.trial_frames(self.data(),trial)
        self.assertEqual(len(rows),8)
        self.assertTrue(all(r['applicable'] for r in rows))
        self.assertEqual({r['finger'] for r in rows},{'Index','Middle'})

    def test_channel_all_notes_off_is_scoped(self):
        d=self.data()
        second=dict(d['events'][0],channel='1',event_index='12')
        reset=dict(d['events'][1],event_type='ControlChange',control='123',channel='0')
        d['events']=[d['events'][0],second,reset]
        spans=a.active_events(d,1,1.6)
        self.assertEqual({int(e['channel']):end for e,_,end in spans},{0:1.45,1:1.6})

    def test_secondary_derivatives_break_at_snapshot_or_loss(self):
        d=self.data()
        rows,_=a.trial_frames(d,self.trial)
        for cb in (2,3):
            for (callback,hand,name),pair in d['joints'].items():
                if callback==cb:
                    pair['snapshot_id']='1'
                    d['raw'][(cb,hand,name)]['snapshot_id']='1'
        d['snapshots'][1]=copy.deepcopy(d['snapshots'][0])
        result=a.side_effects(d,self.trial,rows)
        speeds=[r for r in result if r['metric']=='speed']
        self.assertFalse(any(r['callback_index'] in (2,4) for r in speeds))

    def test_scaled_root_rejected(self):
        path=self.session/'transform_snapshots.jsonl'
        snap=json.loads(path.read_text())
        snap['keyboard_root']['local_to_world'][0]=2
        path.write_text(json.dumps(snap)+'\n')
        with self.assertRaisesRegex(ValueError,'Scaled'):
            self.data()

    def test_incomplete_writer_rejected(self):
        for patch in (dict(writer_closed=False),dict(queue_overflows=1),dict(error='failure')):
            status=dict(writer_closed=True,queue_overflows=0,error='')
            status.update(patch)
            (self.session/'research_recording_summary.json').write_text(json.dumps(status))
            with self.assertRaises(ValueError):
                self.data()

    def test_mixed_sessions_duplicate_rows(self):
        p=self.session/'quest_hand_raw.csv'
        rows=a.read_csv(p)
        write_csv(p,list(rows[0]),rows+[rows[0]])
        with self.assertRaisesRegex(ValueError,'Duplicate'):
            self.data()
        rows[0]['session_id']='OTHER'
        write_csv(p,list(rows[0]),rows)
        with self.assertRaisesRegex(ValueError,'Mixed'):
            self.data()

    def test_no_pseudoreplication(self):
        rows,_=a.trial_frames(self.data(),self.trial)
        trials,summary=a.summarize(rows*10)
        x=next(r for r in summary if r['group']=='all' and r['axis']=='x')
        self.assertEqual(x['valid_trials'],1)
        self.assertIsNone(x['delta_mean_mm_ci_low'])
        self.assertEqual(x['participants'],1)

    def test_improved_worsened_unchanged_trial_statistics(self):
        rows,_=a.trial_frames(self.data(),self.trial)
        for r,delta in zip(rows,(.001,-.001,0.,0.)):
            r['delta_x_m']=delta
        trials,_=a.summarize(rows)
        x=next(r for r in trials if r['group']=='all' and r['axis']=='x')
        self.assertEqual(x['improved_rate'],.25)
        self.assertEqual(x['worsened_rate'],.25)
        self.assertEqual(x['unchanged_rate'],.5)

    def test_missing_referenced_midi_rejected(self):
        d=self.data()
        d['rules'][(1,'Right','Index')]['event_index']='999'
        with self.assertRaisesRegex(ValueError,'referenced MIDI'):
            a.trial_frames(d,self.trial)

    def test_calibration_verification_required(self):
        with self.assertRaisesRegex(ValueError,'Calibration not'):
            a.trial_frames(self.data(),dict(self.trial,calibration_verified=False))

    def test_empty_template_and_overwrite_rejected(self):
        self.config.write_text(json.dumps(dict(schema_version=1,trials=[])))
        result=a.analyze(self.config,self.root/'empty')
        self.assertFalse(result['measured'])
        self.assertIn('NO MEASURED DATA',(self.root/'empty'/'01_xyz_comparison.svg').read_text())
        with self.assertRaisesRegex(ValueError,'exists'):
            a.analyze(self.config,self.root/'empty')

    def test_overlap_output_rejected(self):
        with self.assertRaisesRegex(ValueError,'overlaps'):
            a.analyze(self.config,self.session/'out')

    def test_nonfinite(self):
        with self.assertRaises(ValueError):
            a.finite('nan')

    def test_inventory_cli_never_assigns_fingers(self):
        out=self.root/'inventory'
        result=subprocess.run([sys.executable,str(Path(a.__file__)),'--inspect-session',str(self.session),'--output',str(out)],capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stderr)
        inventory=json.loads((out/'session_inventory.json').read_text())
        self.assertEqual(len(inventory['assignments_to_verify_from_video']),1)
        self.assertIsNone(inventory['assignments_to_verify_from_video'][0]['finger'])

    def test_truncated_csv_row_rejected(self):
        p=self.session/'quest_hand_raw.csv'
        with p.open('a') as f:
            f.write('SYNTHETIC_ONLY,1\n')
        with self.assertRaisesRegex(ValueError,'malformed'):
            self.data()


if __name__=='__main__':
    unittest.main()
