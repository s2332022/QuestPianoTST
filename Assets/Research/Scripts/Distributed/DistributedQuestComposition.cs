using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestComposition : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;VirtualPianoKeyboard m_Keyboard;PianoCalibrationManager m_Calibration;MinimalHandVisualizer m_Visualizer;
        void Start()
        {
            m_Hands=GetComponent<XRHandPoseProvider>();m_Keyboard=GetComponent<VirtualPianoKeyboard>();m_Calibration=GetComponent<PianoCalibrationManager>();m_Visualizer=GetComponent<MinimalHandVisualizer>();
            var piano=GameObject.Find("Piano Root");if(m_Keyboard!=null&&piano!=null)m_Keyboard.ConfigureMinimal(piano.transform);m_Visualizer?.Initialize(m_Hands);m_Calibration?.Initialize(m_Hands);
            if(m_Calibration!=null&&m_Keyboard!=null){m_Calibration.CalibrationChanged+=m_Keyboard.ApplyCalibration;if(m_Calibration.Current.valid)m_Keyboard.ApplyCalibration(m_Calibration.Current);}
        }
        void OnDestroy(){if(m_Calibration!=null&&m_Keyboard!=null)m_Calibration.CalibrationChanged-=m_Keyboard.ApplyCalibration;}
    }
}
