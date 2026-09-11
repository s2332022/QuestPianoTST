using UnityEngine;
using UnityEngine.UI;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestUi : MonoBehaviour
    {
        DistributedQuestClient m_Client;DistributedSettings m_Settings;PianoCalibrationManager m_Calibration;Text m_Status;InputField m_Ip;float m_NextUpdate;
        void Start(){m_Client=FindFirstObjectByType<DistributedQuestClient>();m_Settings=FindFirstObjectByType<DistributedSettings>();m_Calibration=FindFirstObjectByType<PianoCalibrationManager>();Build();}
        void Update(){if(Time.unscaledTime<m_NextUpdate||m_Client==null)return;m_NextUpdate=Time.unscaledTime+0.25f;m_Status.text=$"PC Connected: {m_Client.PcConnected}\nLast Heartbeat: {m_Client.LastHeartbeatSeconds:F3}\nRTT: {m_Client.RttSeconds*1000d:F2} ms\nClock Sync Status: {(m_Client.ClockSynchronized?"Synchronized":"Unavailable")}\nPose Send Rate: {m_Client.PoseSendRate:F1}/s\nMIDI Receive Rate: {m_Client.MidiReceiveRate:F1}/s\nLast MIDI Event: {m_Client.LastMidiEvent}\nSession State: {m_Client.SessionState}";}
        void Build()
        {
            var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;var rect=(RectTransform)transform;rect.sizeDelta=new Vector2(720,620);rect.localScale=Vector3.one*0.0009f;rect.localPosition=new Vector3(0,0.05f,0.55f);gameObject.AddComponent<CanvasScaler>();gameObject.AddComponent<GraphicRaycaster>();
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");m_Status=CreateText("Status",new Vector2(20,-20),new Vector2(680,300),font,24);m_Ip=CreateInput("PC IP",new Vector2(20,-340),new Vector2(360,44),font);m_Ip.text=m_Settings.pcIpAddress;
            CreateButton("Connect",new Vector2(400,-340),font,()=>{m_Settings.pcIpAddress=m_Ip.text;m_Client.StopNetwork();m_Client.StartNetwork();});CreateButton("Disconnect",new Vector2(540,-340),font,m_Client.StopNetwork);
            CreateButton("Calibration A",new Vector2(20,-410),font,()=>m_Calibration?.CaptureA());CreateButton("Calibration B",new Vector2(180,-410),font,()=>m_Calibration?.CaptureB());CreateButton("Calibration C",new Vector2(340,-410),font,()=>m_Calibration?.CaptureC());CreateButton("Save Calibration",new Vector2(500,-410),font,()=>m_Calibration?.SaveCalibration());
        }
        Text CreateText(string name,Vector2 pos,Vector2 size,Font font,int fontSize){var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(transform,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;var t=go.GetComponent<Text>();t.font=font;t.fontSize=fontSize;t.color=Color.white;t.alignment=TextAnchor.UpperLeft;return t;}
        InputField CreateInput(string name,Vector2 pos,Vector2 size,Font font){var bg=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(InputField));bg.transform.SetParent(transform,false);var r=(RectTransform)bg.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;bg.GetComponent<Image>().color=Color.white;var text=CreateText(name+" Text",Vector2.zero,size-new Vector2(12,4),font,22);text.transform.SetParent(bg.transform,false);text.color=Color.black;var input=bg.GetComponent<InputField>();input.textComponent=text;return input;}
        void CreateButton(string name,Vector2 pos,Font font,UnityEngine.Events.UnityAction action){var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(transform,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=new Vector2(130,44);go.GetComponent<Image>().color=new Color(0.15f,0.35f,0.6f);var t=CreateText(name+" Label",Vector2.zero,r.sizeDelta,font,18);t.transform.SetParent(go.transform,false);t.alignment=TextAnchor.MiddleCenter;go.GetComponent<Button>().onClick.AddListener(action);}
    }
}
