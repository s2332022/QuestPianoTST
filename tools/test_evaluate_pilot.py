"""SYNTHETIC TEST DATA ONLY. No measured performance or device validation."""
import copy
import hashlib
import json
import shutil
import tempfile
import unittest
from pathlib import Path

import analyze_rule_effect as old
import evaluate_pilot as p
from analyze_raw_session import write_csv
from test_analyze_rule_effect import fixture


def synthetic(root):
    session,_,trial=fixture(root)
    settings=json.loads((session/'rule_pose_settings.jsonl').read_text())
    common=json.loads(settings['common_settings_json'])
    common.update(enabled=True,engageSeconds=.15,releaseSeconds=.3,planarToleranceMeters=.001)
    rule=json.loads(settings['rule_settings_json'])
    rule.update(segmentRadiusMeters=.001,permittedPenetrationMeters=.003,interferenceRadiusMeters=.002)
    for finger in rule['fingers']:
        finger['maximumChangeFromRawDegrees']=25
    settings.update(common_settings_json=json.dumps(common),rule_settings_json=json.dumps(rule))
    (session/'rule_pose_settings.jsonl').write_text(json.dumps(settings)+'\n')
    original_raw=old.read_csv(session/'quest_hand_raw.csv')
    original_pairs=old.read_csv(session/'quest_rule_pose_joints.csv')
    original_contacts=old.read_csv(session/'quest_rule_pose.csv')
    raw,pairs,contacts=[],[],[]
    for cb,t in enumerate((1.,1.1,1.2,1.3,1.4,1.5,1.6)):
        current_raw=next(r for r in original_raw if int(r['callback_index'])==cb)
        current_pair=next(r for r in original_pairs if int(r['callback_index'])==cb)
        current_contact=next(r for r in original_contacts if int(r['callback_index'])==cb)
        names=['Wrist','Palm']
        for finger in old.FINGERS:
            names.extend(p.chain(finger))
            if finger!='Thumb':
                names.append(finger+'Metacarpal')
        for j,name in enumerate(names):
            f=next((f for f in old.FINGERS if name.startswith(f)),None)
            x=.03+old.FINGERS.index(f)*.04 if f else 0.
            z=.02+(p.chain(f).index(name) if f and name in p.chain(f) else -1)*.01
            # All chains straight, separated; correction on Index only.
            delta=-.015 if f=='Index' else 0.
            r=dict(current_raw,joint_id=str(j),joint_name=name,position_x=str(x),position_y='.01',position_z=str(z))
            pair=dict(current_pair,joint_id=str(j),joint_name=name,raw_x=str(x),raw_y='.01',raw_z=str(z),
                      corrected_x=str(x+delta),corrected_y='.01',corrected_z=str(z))
            raw.append(r)
            pairs.append(pair)
        for finger in old.FINGERS:
            pair=next(r for r in pairs if int(r['callback_index'])==cb and r['joint_name']==finger+'Tip')
            contact=dict(current_contact)
            contact.update({k:v for k,v in pair.items() if k.startswith(('raw_','corrected_')) or k in ('joint_id','joint_name')})
            contact['finger']=finger
            contact['state']='Engaging' if 1.05<=t<1.2 else 'Active' if 1.2<=t<1.45 else 'Releasing' if t>=1.45 else 'Inactive'
            if finger!='Index':
                contact.update(note='-1',source='',onset_event_index='-1',event_index='-1',gain='0',state='Inactive')
            contacts.append(contact)
    for name,rows in (('quest_hand_raw.csv',raw),('quest_rule_pose_joints.csv',pairs),('quest_rule_pose.csv',contacts)):
        write_csv(session/name,list(rows[0]),rows)
    metadata=json.loads((session/'session_metadata.json').read_text())
    metadata.update(start_utc='2030-01-01T00:00:00Z',git_commit_hash='SYNTHETIC_COMMIT',git_dirty=True,
                    unity_version='6000.5.10f1',build_timestamp_utc='2029-12-31T00:00:00Z')
    (session/'session_metadata.json').write_text(json.dumps(metadata))
    protocol=json.loads((Path(__file__).parent.parent/'docs/pilot_protocol.defaults.json').read_text())
    protocol['max_gap_s']=.100001
    locked=dict(schema_version=1,locked_at_utc='2029-12-31T00:00:00Z',protocol=protocol,
                protocol_sha256=hashlib.sha256(p.canonical(protocol)).hexdigest())
    (root/'lock.json').write_text(json.dumps(locked))
    trial.update(method='FullRules',logged_method='FullRules',hand='Right',build_manifest='synthetic.apk.provenance.json',apk_path='synthetic.apk')
    artifact=root/'synthetic.apk'
    artifact.write_bytes(b'SYNTHETIC TEST ARTIFACT; NOT AN APK')
    manifest=dict(schema_version=1,artifact_sha256=p.digest(artifact),artifact_bytes=artifact.stat().st_size,
                  scene='Assets/Research/Scenes/PianoDistributedQuest.unity',target='Android',unity_version='6000.5.10f1',
                  build_timestamp_utc=metadata['build_timestamp_utc'],git_status_porcelain=' M synthetic',
                  build_metadata=dict(git_commit_hash='SYNTHETIC_COMMIT',git_dirty='true'))
    (root/'synthetic.apk.provenance.json').write_text(json.dumps(manifest))
    points=[dict(session_id=metadata['session_id'],calibration_id='cal1',point_id='SYNTHETIC_V'+str(i),used_for_fit=False,
                 measurement_method='external',reference_keyboard_m=[i*.02,0,0],observed_keyboard_m=[i*.02,0,0],
                 reference_survey='SYNTHETIC',registration_reference='SYNTHETIC',uncertainty_mm=1) for i in range(4)]
    (root/'calibration.json').write_text(json.dumps(dict(points=points)))
    synchronization=dict(session_id=metadata['session_id'],video_frame_ms=10,display_latency_bound_ms=0,
         anchors=[dict(anchor_id=str(i),kind='independent_shared_event',video_sec=i*10.,quest_sec=.5+i*10.,uncertainty_ms=1,evidence='SYNTHETIC') for i in range(3)],
         contacts=[dict(source='BLE',generation=1,onset_event_index=10,video_contact_sec=.54)])
    (root/'sync.json').write_text(json.dumps(dict(sessions=[synchronization])))
    config=dict(schema_version=2,data_kind='synthetic_test',protocol_lock='lock.json',protocol_sha256=locked['protocol_sha256'],
                calibration_file='calibration.json',synchronization_file='sync.json',trials=[trial])
    config_path=root/'conditions.json'
    config_path.write_text(json.dumps(config))
    return session,trial,protocol,config_path


class PilotTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='SYNTHETIC_PILOT_ONLY_')
        self.root=Path(self.temp.name)
        self.session,self.trial,self.protocol,self.config=synthetic(self.root)
        self.data=old.load_session(self.session)

    def tearDown(self):
        self.temp.cleanup()

    def rows(self,data=None,trial=None):
        return p.metric_rows(data or self.data,trial or self.trial,self.protocol)

    def test_phases_causal_approach_and_all_fingers(self):
        rows,_,_,issues=self.rows()
        self.assertEqual(len(rows),30)
        self.assertEqual(issues,[])
        approach=next(r for r in rows if r['finger']=='Index' and r['phase']=='approach')
        self.assertNotIn('display_surface_residual_mm',approach)
        self.assertFalse(approach['future_target_used'])
        self.assertGreater(approach['tip_cost_mm'],0) # detect unintended early movement
        self.assertEqual({r['phase'] for r in rows},{'approach','engage','hold','release','nonpressing'})

    def test_display_stop_and_release_never_replaced_with_raw(self):
        for row in self.data['rules'].values():
            row['gain']='0'
            row['reason']='await_all_notes_off'
        rows,_,_,_=self.rows()
        stopped=[r for r in rows if r['stopped']]
        self.assertTrue(stopped)
        self.assertTrue(all(r['tip_cost_mm']>0 for r in stopped))
        self.assertTrue(any(r['phase']=='release' and r['tip_cost_mm']>0 for r in rows))

    def test_invalid_tracking_missing_not_fabricated(self):
        self.data['raw'][(2,'Right','IndexTip')]['tracking_valid']=False
        rows,_,_,_=self.rows()
        row=next(r for r in rows if r['callback_index']==2 and r['finger']=='Index')
        self.assertFalse(row['valid'])
        self.assertNotIn('tip_cost_mm',row)
        after=next(r for r in rows if r['callback_index']==3 and r['finger']=='Index')
        self.assertNotIn('offset_speed_mm_s',after)

    def test_nonuniform_derivatives_and_midpoint_times(self):
        protocol=dict(self.protocol,max_gap_s=1.)
        prev=None
        result=[]
        # x=t^2 yields exact midpoint velocities and constant acceleration 2.
        for t in (0.,.1,.25,.5,.8):
            prev,values=p.derivative_step(prev,t,0,dict(raw=(t*t,0,0)),protocol)
            result.append(values)
        self.assertAlmostEqual(result[-1]['raw_acceleration_mm_s2'],2000)
        self.assertAlmostEqual(result[-1]['raw_jerk_mm_s3'],0,places=8)
        self.assertAlmostEqual(result[1]['raw_speed_mm_s'],100)
        _,values=p.derivative_step(prev,1.,1,dict(raw=(1,0,0)),protocol)
        self.assertFalse(values)

    def test_gap_breaks_derivatives(self):
        previous,_=p.derivative_step(None,0,0,dict(offset=(0,0,0)),self.protocol)
        _,values=p.derivative_step(previous,.5,0,dict(offset=(1,0,0)),self.protocol)
        self.assertFalse(values)

    def test_segment_interference_geometry(self):
        self.assertAlmostEqual(p.segment_distance((0,0,0),(1,0,0),(.5,-1,0),(.5,1,0)),0)
        self.assertAlmostEqual(p.segment_distance((0,0,0),(1,0,0),(0,1,0),(1,1,0)),1)
        self.assertAlmostEqual(p.segment_distance((0,0,0),(0,0,0),(1,0,0),(1,0,0)),1)

    def test_cost_bone_constraints_and_nonpressing(self):
        rows,joints,_,_=self.rows()
        index=next(r for r in rows if r['finger']=='Index' and r['role']=='pressing')
        self.assertAlmostEqual(index['tip_cost_mm'],15)
        self.assertAlmostEqual(index['bone_change_mm'],0)
        self.assertAlmostEqual(index['offset_x_mm'],-15)
        self.assertTrue(any(r['role']=='nonpressing' for r in rows))
        self.assertTrue(all(r['cost_mm']>=0 for r in joints))

    def test_small_improvement_ratio_undefined(self):
        rows,_,_,_=self.rows()
        for row in rows:
            if row.get('improvement_mm') is not None and row['improvement_mm']<=self.protocol['ratio_min_improvement_mm']:
                self.assertIsNone(row['cost_per_improvement'])

    def test_raw_off_condition_and_legacy_supported(self):
        for method in ('Raw','Legacy'):
            data=copy.deepcopy(self.data)
            trial=dict(self.trial,method=method,logged_method=method)
            data['settings'][1]['common']['enabled']=method!='Raw'
            for row in list(data['joints'].values())+list(data['rules'].values()):
                row.update(requested_method=method,executed_method=method)
                if method=='Raw':
                    for axis in 'xyz':
                        row['corrected_'+axis]=row['raw_'+axis]
                    row['enabled']='False'
            rows,_,_,_=self.rows(data,trial)
            self.assertTrue(rows)
            if method=='Raw':
                self.assertTrue(all(not r['stopped'] and r.get('tip_cost_mm',0)==0 for r in rows))

    def test_udp_rejected(self):
        self.data['events'][0]['source']='UDP'
        with self.assertRaisesRegex(ValueError,'UDP'):
            p.require_ble(self.data)

    def test_missing_note_off_quality_issue(self):
        self.data['events']=self.data['events'][:1]
        spans,issues=p.midi_audit(self.data,1,1.6)
        self.assertTrue(spans)
        self.assertIn('missing_note_off',issues)

    def test_transitions_censoring_and_release_growth(self):
        rows,_,spans,_=self.rows()
        result=p.transitions(rows,spans,self.protocol)[0]
        self.assertAlmostEqual(result['first_applied_ms'],50)
        self.assertTrue(result['release_censored'])
        self.assertEqual(result['release_max_growth_mm'],0)

    def test_trial_stats_p95_and_not_frame_replications(self):
        rows,_,_,_=self.rows()
        summaries=p.trial_summary(rows*3)
        population=p.aggregate(summaries)
        metric=next(r for r in summaries if r['metric']=='tip_cost_mm' and r['finger']=='Index')
        self.assertIn('p95',metric)
        self.assertIn('maximum',metric)
        self.assertTrue(all(r['trials']==1 and r['participants']==1 for r in population))
        self.assertTrue(all(r['ci_low'] is None for r in population))

    def test_calibration_quest_not_independent_and_heldout(self):
        record=dict(session_id='SYNTHETIC_ONLY',calibration_id='cal1',point_id='V1',used_for_fit=False,
            reference_keyboard_m=[0,0,0],reference_survey='SYNTHETIC',measurement_method='quest_tip',callback_index=1,hand='Right',joint='IndexTip')
        row=p.calibration(self.data,self.trial,[record])[0]
        self.assertFalse(row['independent_of_quest_tracking'])
        self.assertGreater(row['residual_mm'],0)
        with self.assertRaisesRegex(ValueError,'Held-out'):
            p.calibration(self.data,self.trial,[dict(record,used_for_fit=True)])

    def test_sync_affine_map_and_proxy_bias(self):
        record=json.loads((self.root/'sync.json').read_text())['sessions'][0]
        summary,delays=p.synchronize(self.data,self.trial,record,self.protocol)
        self.assertAlmostEqual(summary['clock_intercept_s'],.5)
        self.assertAlmostEqual(delays[0]['contact_to_receive_ms'],0)
        self.assertAlmostEqual(delays[0]['receive_to_observed_ms'],10)
        for a in record['anchors']:
            a['kind']='display_event_proxy'
        record['display_latency_bound_ms']=30
        summary,delays=p.synchronize(self.data,self.trial,record,self.protocol)
        self.assertFalse(summary['independent_clock_alignment'])
        self.assertEqual(summary['alignment_uncertainty_ms'],36)

    def test_full_pipeline_and_five_trial_gate_not_invented(self):
        before={p.name:p.read_bytes() for p in self.session.iterdir()}
        result=p.analyze(self.config,self.root/'out')
        self.assertFalse(result['measured'])
        self.assertFalse(result['ready_for_main_experiment'])
        self.assertEqual(result['observed_trials'],1)
        self.assertTrue(result['all_trials_passed'])
        metrics=old.read_csv(self.root/'out'/'trial_metrics.csv')
        self.assertTrue(any(r['metric']=='active_state_ms' and r['unit']=='ms' for r in metrics))
        self.assertEqual(before,{p.name:p.read_bytes() for p in self.session.iterdir()})
        import xml.etree.ElementTree as ET
        for svg in (self.root/'out').glob('*.svg'):
            ET.parse(svg)

    def test_preregistration_hash_and_time_enforced(self):
        lock=json.loads((self.root/'lock.json').read_text())
        lock['protocol']['max_stopped_rate']=.5
        (self.root/'lock.json').write_text(json.dumps(lock))
        with self.assertRaisesRegex(ValueError,'thresholds changed'):
            p.analyze(self.config,self.root/'out')

    def test_late_lock_rejected(self):
        lock=json.loads((self.root/'lock.json').read_text())
        lock['locked_at_utc']='2031-01-01T00:00:00Z'
        (self.root/'lock.json').write_text(json.dumps(lock))
        with self.assertRaisesRegex(ValueError,'before recording'):
            p.analyze(self.config,self.root/'out')

    def test_artifact_runtime_mismatch_fails_traceability(self):
        self.assertTrue(p.provenance(self.data,self.trial,self.root)[0])
        (self.root/'synthetic.apk').write_bytes(b'changed')
        self.assertFalse(p.provenance(self.data,self.trial,self.root)[0])

    def test_protocol_lock_refuses_overwrite(self):
        protocol=Path(__file__).parent.parent/'docs/pilot_protocol.defaults.json'
        p.lock_protocol(protocol,self.root/'new.lock')
        with self.assertRaisesRegex(ValueError,'exists'):
            p.lock_protocol(protocol,self.root/'new.lock')

    def test_five_synthetic_trials_quality_gate_and_same_subject(self):
        config=json.loads(self.config.read_text())
        cal=json.loads((self.root/'calibration.json').read_text())
        sync=json.loads((self.root/'sync.json').read_text())
        config['trials']=[]
        all_cal,all_sync=[],[]
        for i,task in enumerate(old.TASKS):
            sid='SYNTHETIC_ONLY_'+str(i)
            target=self.root/('synthetic_session_'+str(i))
            shutil.copytree(self.session,target)
            meta=json.loads((target/'session_metadata.json').read_text())
            meta['session_id']=sid
            (target/'session_metadata.json').write_text(json.dumps(meta))
            for name in ('quest_hand_raw.csv','quest_rule_pose.csv','quest_rule_pose_joints.csv','quest_midi_research.csv'):
                path=target/name
                rows=old.read_csv(path)
                for r in rows:
                    r['session_id']=sid
                write_csv(path,list(rows[0]),rows)
            trial=dict(self.trial,trial_id='SYNTHETIC_T_'+str(i),session_id=sid,session_dir=target.name,task=task)
            config['trials'].append(trial)
            all_cal.extend(dict(r,session_id=sid) for r in cal['points'])
            all_sync.append(dict(sync['sessions'][0],session_id=sid))
        (self.root/'calibration.json').write_text(json.dumps(dict(points=all_cal)))
        (self.root/'sync.json').write_text(json.dumps(dict(sessions=all_sync)))
        self.config.write_text(json.dumps(config))
        result=p.analyze(self.config,self.root/'five')
        self.assertTrue(result['data_quality_passed']) # SYNTHETIC gate logic, not a real pilot result
        self.assertFalse(result['ready_for_main_experiment'])
        population=old.read_csv(self.root/'five'/'method_summary.csv')
        self.assertTrue(all(r['participants']=='1' for r in population))
        self.assertFalse(result['physical_device_verified'])

    def test_release_completion_and_growth(self):
        rows,_,spans,_=self.rows()
        index=next(r for r in rows if r['finger']=='Index' and r['phase']=='hold')
        rows=[r for r in rows if r['timestamp']<1.45]
        for t,cost in ((1.46,20),(1.50,0),(1.55,0),(1.61,0)):
            rows.append(dict(index,timestamp=t,phase='release',role='nonpressing',tip_cost_mm=cost))
        result=p.transitions(rows,spans,dict(self.protocol,max_gap_s=.11))[0]
        self.assertFalse(result['release_censored'])
        self.assertAlmostEqual(result['release_completion_ms'],50)
        self.assertAlmostEqual(result['release_max_growth_mm'],5)

    def test_repeated_calibration_coordinate_not_four_points(self):
        cal=json.loads((self.root/'calibration.json').read_text())
        for r in cal['points']:
            r['reference_keyboard_m']=[0,0,0]
            r['observed_keyboard_m']=[0,0,0]
        (self.root/'calibration.json').write_text(json.dumps(cal))
        p.analyze(self.config,self.root/'bad_cal')
        quality=old.read_csv(self.root/'bad_cal'/'pilot_quality.csv')[0]
        self.assertEqual(quality['heldout_calibration'],'False')

    def test_init_study_empty_outputs_are_not_measured(self):
        study=self.root/'new_study'
        p.init_study(study)
        result=p.analyze(study/'conditions.json',study/'empty_results')
        self.assertFalse(result['measured'])
        self.assertFalse(result['ready_for_main_experiment'])
        self.assertIn('NO MEASURED DATA',(study/'empty_results'/'residual_vs_cost.svg').read_text())

    def test_missing_whole_callback_detected_as_gap(self):
        for name in ('quest_hand_raw.csv','quest_rule_pose_joints.csv','quest_rule_pose.csv'):
            path=self.session/name
            rows=[r for r in old.read_csv(path) if int(r['callback_index'])!=2]
            write_csv(path,list(rows[0]),rows)
        p.analyze(self.config,self.root/'gap')
        quality=old.read_csv(self.root/'gap'/'pilot_quality.csv')[0]
        self.assertEqual(quality['callback_gaps'],'False')
        self.assertGreater(float(quality['max_callback_gap_ms']),100)


if __name__=='__main__':
    unittest.main()
