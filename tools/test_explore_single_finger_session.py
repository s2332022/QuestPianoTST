"""Synthetic tests of the exploratory-only single-finger diagnostic."""
import tempfile
import unittest
from pathlib import Path

import analyze_rule_effect as formal
import explore_single_finger_session as diagnostic
from test_analyze_rule_effect import fixture


class ExploratoryTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        session,_,self.trial=fixture(Path(self.temp.name))
        self.data=formal.load_session(session)
        settings=self.data['settings'][1]
        settings['common'].update(enabled=True,finger=1)
        settings['rule']['manualNoteMap']=False
        for row in self.data['rules'].values():
            row.update(angular_violation_deg='0',tip_target_residual_m='0')
            if row['state']=='Holding':row['state']='Active'

    def tearDown(self):
        self.temp.cleanup()

    def test_formal_video_gate_is_not_weakened(self):
        self.trial['video_verified']=False
        with self.assertRaisesRegex(ValueError,'not video verified'):
            formal.trial_frames(self.data,self.trial)

    def test_explicit_note_off_and_full_key_down_population(self):
        spans=diagnostic.paired_spans(self.data['events'],'BLE',1,10,10)
        rows,_=diagnostic.extract_frames(self.data,spans,'Right','Index',1)
        trials,summary=diagnostic.summarize(rows,spans)
        self.assertEqual(trials[0]['frames'],4)
        self.assertEqual(trials[0]['valid_frames'],4)
        first=next(r for r in rows if r['strike']==1)
        self.assertAlmostEqual(first['raw_pos_x_mm'],30)
        self.assertAlmostEqual(first['raw_x_mm'],10)
        y=next(x for x in summary if x['axis']=='y')
        self.assertAlmostEqual(y['raw_mean_mm'],10)
        self.assertAlmostEqual(y['corrected_mean_mm'],1)
        self.assertAlmostEqual(y['improvement_pct'],90)
        self.assertEqual(y['improved_pct'],100)

    def test_unapplied_frame_is_retained_in_denominator(self):
        spans=diagnostic.paired_spans(self.data['events'],'BLE',1,10,10)
        self.data['rules'][(1,'Right','Index')]['state']='Inactive'
        rows,_=diagnostic.extract_frames(self.data,spans,'Right','Index',1)
        trials,_=diagnostic.summarize(rows,spans)
        self.assertEqual(trials[0]['frames'],4)
        self.assertEqual(trials[0]['applicable_frames'],3)
        self.assertIn('state_Inactive:1',trials[0]['not_applied_reasons'])

    def test_missing_noteoff_is_not_invented(self):
        with self.assertRaisesRegex(ValueError,'Missing or ambiguous explicit Note Off'):
            diagnostic.paired_spans(self.data['events'][:1],'BLE',1,10,10)

    def test_zero_reference_and_tolerance(self):
        self.assertIsNone(diagnostic.ratio(0,1))
        self.assertEqual(diagnostic.classify(.0005),'unchanged')
        self.assertEqual(diagnostic.classify(-.002),'worsened')
        self.assertEqual(diagnostic.classify(.002),'improved')

    def test_release_window_is_bounded_by_next_onset(self):
        spans=diagnostic.paired_spans(self.data['events'],'BLE',1,10,10)
        rows,sides=diagnostic.extract_frames(self.data,spans,'Right','Index',1)
        releases=diagnostic.release_summary(rows,sides,spans)
        self.assertEqual(releases[0]['releasing_frames'],2)
        self.assertAlmostEqual(releases[0]['off_sec'],1.45)


if __name__=='__main__':
    unittest.main()
