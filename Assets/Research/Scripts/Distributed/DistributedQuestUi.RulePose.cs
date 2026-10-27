using System;
using TMPro;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed partial class DistributedQuestUi
    {
        int m_RuleMapNote=60;
        readonly TMP_Text[] m_RuleDetails=new TMP_Text[3];
        TMP_Text m_RuleMethodButton;
        void ChangeRule(Action<MidiTipCorrectionSettings,RuleHandPoseSettings> change)
        {if(m_Client!=null)change(m_Client.CorrectionSettings,m_Client.RuleSettings);}
        FingerPoseRule SelectedRule(MidiTipCorrectionSettings c,RuleHandPoseSettings s)=>s.fingers[Mathf.Clamp((int)c.finger,0,4)];
        TMP_Text RuleButton(string name,int page,int slot,UnityEngine.Events.UnityAction action,bool confirm=false)
        {
            var p=new Vector2(20+(slot%2)*350,-540-(slot/2)*75);
            var label=CreateButton(name,p,330,action,confirm);Put(name,page,p.x,p.y,330,60);return label;
        }
        static void Preset(RuleHandPoseSettings s)
        {
            // The primary selector keeps research presets simple and protects the Legacy default.
            s.method=s.method==HandCorrectionMethod.Legacy?HandCorrectionMethod.FullRules:HandCorrectionMethod.Legacy;
            if(s.method==HandCorrectionMethod.Legacy || s.method==HandCorrectionMethod.Raw)return;
            s.contact=true;s.joints=s.method!=HandCorrectionMethod.ContactOnly;
            s.nonpenetration=s.interference=s.method==HandCorrectionMethod.FullRules;
        }
        void BuildRuleControls()
        {
            CreateButton("NEW RULE SETTINGS",new Vector2(20,-1010),680,()=>ShowDetails(true,5));Put("NEW RULE SETTINGS",2,20,-1010,680,60);
            Put("Correction Instructions",2,20,-1090,680,145);
            m_RuleMethodButton=RuleButton("METHOD NEXT",5,0,()=>ChangeRule((c,s)=>Preset(s)));
            RuleButton("NOTE MAP / LIMITS",5,1,()=>ShowDetails(true,6));
            RuleButton("R1 CONTACT ON/OFF",5,2,()=>ChangeRule((c,s)=>s.contact=!s.contact));
            RuleButton("JOINT RULES ON/OFF",5,3,()=>ChangeRule((c,s)=>{s.joints=!s.joints;if(s.joints&&s.method==HandCorrectionMethod.ContactOnly)s.method=HandCorrectionMethod.JointConstraints;}));
            RuleButton("R2 NONPEN ON/OFF",5,4,()=>ChangeRule((c,s)=>{s.nonpenetration=!s.nonpenetration;if(s.nonpenetration&&s.method!=HandCorrectionMethod.Legacy&&s.method!=HandCorrectionMethod.Raw)s.method=HandCorrectionMethod.FullRules;}));
            RuleButton("INTERFERENCE ON/OFF",5,5,()=>ChangeRule((c,s)=>{s.interference=!s.interference;if(s.interference&&s.method!=HandCorrectionMethod.Legacy&&s.method!=HandCorrectionMethod.Raw)s.method=HandCorrectionMethod.FullRules;}));
            RuleButton("ENGAGE -20 ms",5,6,()=>ChangeRule((c,s)=>c.engageSeconds=Mathf.Max(.02f,c.engageSeconds-.02f)));
            RuleButton("ENGAGE +20 ms",5,7,()=>ChangeRule((c,s)=>c.engageSeconds=Mathf.Min(1,c.engageSeconds+.02f)));
            RuleButton("RELEASE -20 ms",5,8,()=>ChangeRule((c,s)=>c.releaseSeconds=Mathf.Max(.02f,c.releaseSeconds-.02f)));
            RuleButton("RELEASE +20 ms",5,9,()=>ChangeRule((c,s)=>c.releaseSeconds=Mathf.Min(1,c.releaseSeconds+.02f)));
            RuleButton("RADIUS -1 mm",5,10,()=>ChangeRule((c,s)=>{s.segmentRadiusMeters=Mathf.Max(0,s.segmentRadiusMeters-.001f);s.interferenceRadiusMeters=Mathf.Max(0,s.interferenceRadiusMeters-.001f);}));
            RuleButton("RADIUS +1 mm",5,11,()=>ChangeRule((c,s)=>{s.segmentRadiusMeters=Mathf.Min(.015f,s.segmentRadiusMeters+.001f);s.interferenceRadiusMeters=Mathf.Min(.015f,s.interferenceRadiusMeters+.001f);}));
            RuleButton("DEPTH -1 mm",5,12,()=>ChangeRule((c,s)=>s.permittedPenetrationMeters=Mathf.Max(0,s.permittedPenetrationMeters-.001f)));
            RuleButton("DEPTH +1 mm",5,13,()=>ChangeRule((c,s)=>s.permittedPenetrationMeters=Mathf.Min(.03f,s.permittedPenetrationMeters+.001f)));
            RuleButton("SMOOTH -5 ms",5,14,()=>ChangeRule((c,s)=>s.smoothingSeconds=Mathf.Max(0,s.smoothingSeconds-.005f)));
            RuleButton("SMOOTH +5 ms",5,15,()=>ChangeRule((c,s)=>s.smoothingSeconds=Mathf.Min(.2f,s.smoothingSeconds+.005f)));
            RuleButton("MANUAL NOTE MAP ON/OFF",6,0,()=>ChangeRule((c,s)=>s.manualNoteMap=!s.manualNoteMap));
            RuleButton("BACK TO RULE SETTINGS",6,1,()=>ShowDetails(true,5));
            RuleButton("MAP NOTE -1",6,2,()=>m_RuleMapNote=Mathf.Max(0,m_RuleMapNote-1));
            RuleButton("MAP NOTE +1",6,3,()=>m_RuleMapNote=Mathf.Min(127,m_RuleMapNote+1));
            RuleButton("ASSIGN SELECTED FINGER",6,4,()=>{if(m_Client!=null)m_Client.PoseCorrection.New.TrySetManualAssignment(m_RuleMapNote,(int)m_Client.CorrectionSettings.finger);});
            RuleButton("CLEAR THIS NOTE",6,5,()=>{if(m_Client!=null)m_Client.PoseCorrection.New.TrySetManualAssignment(m_RuleMapNote,-1);});
            RuleButton("FINGER MAX -5 mm",6,6,()=>ChangeRule((c,s)=>SelectedRule(c,s).maxDisplacementMeters=Mathf.Max(0,SelectedRule(c,s).maxDisplacementMeters-.005f)));
            RuleButton("FINGER MAX +5 mm",6,7,()=>ChangeRule((c,s)=>SelectedRule(c,s).maxDisplacementMeters=Mathf.Min(.05f,SelectedRule(c,s).maxDisplacementMeters+.005f)));
            RuleButton("HAND RMS -5 mm",6,8,()=>ChangeRule((c,s)=>s.wholeHandRmsLimitMeters=Mathf.Max(0,s.wholeHandRmsLimitMeters-.005f)));
            RuleButton("HAND RMS +5 mm",6,9,()=>ChangeRule((c,s)=>s.wholeHandRmsLimitMeters=Mathf.Min(.05f,s.wholeHandRmsLimitMeters+.005f)));
            RuleButton("RAW CONE -5 deg",6,10,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumChangeFromRawDegrees=Mathf.Max(0,SelectedRule(c,s).maximumChangeFromRawDegrees-5)));
            RuleButton("RAW CONE +5 deg",6,11,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumChangeFromRawDegrees=Mathf.Min(90,SelectedRule(c,s).maximumChangeFromRawDegrees+5)));
            RuleButton("MCP BEND -5 deg",6,12,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.x=Mathf.Max(0,SelectedRule(c,s).maximumBendDegrees.x-5)));
            RuleButton("MCP BEND +5 deg",6,13,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.x=Mathf.Min(180,SelectedRule(c,s).maximumBendDegrees.x+5)));
            RuleButton("MORE JOINTS / PAD",6,14,()=>ShowDetails(true,7));
            RuleButton("CLEAR ALL NOTE MAP",6,15,()=>{if(m_Client!=null)for(var n=0;n<128;n++)m_Client.PoseCorrection.New.TrySetManualAssignment(n,-1);},true);
            RuleButton("PIP BEND -5 deg",7,0,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.y=Mathf.Max(0,SelectedRule(c,s).maximumBendDegrees.y-5)));
            RuleButton("PIP BEND +5 deg",7,1,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.y=Mathf.Min(180,SelectedRule(c,s).maximumBendDegrees.y+5)));
            RuleButton("DIP BEND -5 deg",7,2,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.z=Mathf.Max(0,SelectedRule(c,s).maximumBendDegrees.z-5)));
            RuleButton("DIP BEND +5 deg",7,3,()=>ChangeRule((c,s)=>SelectedRule(c,s).maximumBendDegrees.z=Mathf.Min(180,SelectedRule(c,s).maximumBendDegrees.z+5)));
            RuleButton("FINGER PAD X -1 mm",7,4,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.x=Mathf.Max(-.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.x-.001f)));
            RuleButton("FINGER PAD X +1 mm",7,5,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.x=Mathf.Min(.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.x+.001f)));
            RuleButton("FINGER PAD Y -1 mm",7,6,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.y=Mathf.Max(-.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.y-.001f)));
            RuleButton("FINGER PAD Y +1 mm",7,7,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.y=Mathf.Min(.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.y+.001f)));
            RuleButton("FINGER PAD Z -1 mm",7,8,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.z=Mathf.Max(-.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.z-.001f)));
            RuleButton("FINGER PAD Z +1 mm",7,9,()=>ChangeRule((c,s)=>SelectedRule(c,s).contactOffsetKeyboardMeters.z=Mathf.Min(.03f,SelectedRule(c,s).contactOffsetKeyboardMeters.z+.001f)));
            RuleButton("OFFSET SPEED -0.05",7,10,()=>ChangeRule((c,s)=>s.maximumOffsetSpeedMetersPerSecond=Mathf.Max(0,s.maximumOffsetSpeedMetersPerSecond-.05f)));
            RuleButton("OFFSET SPEED +0.05",7,11,()=>ChangeRule((c,s)=>s.maximumOffsetSpeedMetersPerSecond=Mathf.Min(1,s.maximumOffsetSpeedMetersPerSecond+.05f)));
            RuleButton("ITERATIONS -1",7,12,()=>ChangeRule((c,s)=>s.iterations=Mathf.Max(1,s.iterations-1)));
            RuleButton("ITERATIONS +1",7,13,()=>ChangeRule((c,s)=>s.iterations=Mathf.Min(16,s.iterations+1)));
            RuleButton("BACK TO NOTE MAP",7,14,()=>ShowDetails(true,6));
            RuleButton("RULE NEXT FINGER",7,15,()=>ChangeRule((c,s)=>c.finger=(CorrectionFinger)(((int)c.finger+1)%5)));
            for(var page=5;page<=7;page++)
            {
                var text=CreateText("Rule details "+page,transform,"",new Vector2(20,-1140),new Vector2(680,page==6?160:100),21,TextAlignmentOptions.TopLeft,Color.white);
                m_RuleDetails[page-5]=text;m_DetailItems[text.gameObject]=page;
            }
        }
        void UpdateRuleDetails()
        {
            if(m_Client==null)return;var c=m_Client.CorrectionSettings;var s=m_Client.RuleSettings;
            if(!s.IsValid)return;
            var f=SelectedRule(c,s);var mapped=s.noteToFinger[m_RuleMapNote];
            if(m_RuleMethodButton!=null)m_RuleMethodButton.text="補正方式："+(s.method==HandCorrectionMethod.Legacy?"旧方式":s.method==HandCorrectionMethod.Raw?"補正なし":"新方式")+"（切替）";
            if(m_RuleDetails[0]!=null)m_RuleDetails[0].text=$"{(s.method==HandCorrectionMethod.Legacy?"旧方式":s.method==HandCorrectionMethod.Raw?"補正なし":"新方式")}：接触制約 {s.contact} / 関節制約 {s.UseJoints}\n非貫通制約 {s.UseEnvironment&&s.nonpenetration} / 指同士の干渉 {s.UseEnvironment&&s.interference}\n補正開始 {c.engageSeconds*1000:F0} ms / 補正解除 {c.releaseSeconds*1000:F0} ms / 平滑化 {s.smoothingSeconds*1000:F0} ms\n許容深度 {s.permittedPenetrationMeters*1000:F0} / 半径 {s.segmentRadiusMeters*1000:F0} mm";
            if(m_RuleDetails[1]!=null)m_RuleDetails[1].text=$"手動割当 {(s.manualNoteMap?"ON":"OFF")} / 鍵{m_RuleMapNote}→{(mapped<0?"未割当":RuleHandPoseProcessor.FingerJapanese(mapped))}\n"+
                m_Client.PoseCorrection.New.AssignmentSummaryJapanese+"\n"+m_Client.PoseCorrection.New.AssignmentRejectionJapanese;
            if(m_RuleDetails[2]!=null)m_RuleDetails[2].text=$"{c.finger}の関節角度上限：{f.maximumBendDegrees.x:F0}/{f.maximumBendDegrees.y:F0}/{f.maximumBendDegrees.z:F0}度\n接触位置オフセット XYZ：{f.contactOffsetKeyboardMeters.x*1000:F0}/{f.contactOffsetKeyboardMeters.y*1000:F0}/{f.contactOffsetKeyboardMeters.z*1000:F0} mm\n補正速度上限 {s.maximumOffsetSpeedMetersPerSecond:F2} m/s / 反復 {s.iterations}回\n幾何制約による補正です。実機精度の検証は別途必要です。";
        }
    }
}
