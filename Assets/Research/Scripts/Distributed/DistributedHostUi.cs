using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedHostUi : MonoBehaviour
    {
        DistributedPcHost m_Host;Vector2 m_Scroll;string m_ExportPath="";
        void Start()=>m_Host=FindFirstObjectByType<DistributedPcHost>();
        void OnGUI()
        {
            if(m_Host==null)return;GUILayout.BeginArea(new Rect(12,12,520,Screen.height-24),GUI.skin.box);m_Scroll=GUILayout.BeginScrollView(m_Scroll);
            GUILayout.Label("Quest Piano Motion - Distributed PC Host");GUILayout.Label("PC IP: "+LocalIpv4());GUILayout.Label("Quest IP: "+m_Host.QuestIpAddress);GUILayout.Label("Quest Connected: "+m_Host.QuestConnected);GUILayout.Label("Last Heartbeat: "+m_Host.LastHeartbeat.ToString("F3"));GUILayout.Label("Clock Offset: "+m_Host.ClockOffset.ToString("F6")+" s");GUILayout.Label("RTT: "+(m_Host.Rtt*1000d).ToString("F2")+" ms");GUILayout.Label("Clock Samples: "+m_Host.ClockSamples);GUILayout.Label("Pose receive rate: "+m_Host.PoseReceiveRate.ToString("F1")+" packet/s");GUILayout.Label("Pose packet loss estimate: "+(m_Host.PoseLossEstimate*100d).ToString("F2")+" %");
            var midi=m_Host.Midi;GUILayout.Label("MIDI Device: "+midi.ConnectedDeviceName);GUILayout.Label("MIDI Connected: "+midi.IsConnected);GUILayout.Label("Last MIDI Event: "+midi.LastEventText);GUILayout.Label("Session State: "+m_Host.SessionState);GUILayout.Label("Recording Time: "+(m_Host.Recorder?.RecordingSeconds??0d).ToString("F1")+" s");GUILayout.Label("Save Path: "+m_Host.SavePath);
            if(GUILayout.Button("Refresh MIDI Devices"))midi.RefreshDeviceList();for(var i=0;i<midi.Devices.Count;++i)if(GUILayout.Button("Select: "+midi.Devices[i].Name))midi.SelectDevice(i);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Connect MIDI"))midi.ConnectSelectedDevice();if(GUILayout.Button("Disconnect MIDI"))midi.Disconnect();GUILayout.EndHorizontal();GUILayout.BeginHorizontal();if(GUILayout.Button("Start Network"))m_Host.StartNetwork();if(GUILayout.Button("Stop Network"))m_Host.StopNetwork();GUILayout.EndHorizontal();GUILayout.BeginHorizontal();if(GUILayout.Button("Start Session"))m_Host.StartSession();if(GUILayout.Button("Stop Session"))m_Host.StopSession();GUILayout.EndHorizontal();if(GUILayout.Button("Export Diagnostics"))m_ExportPath=m_Host.ExportDiagnostics();if(!string.IsNullOrEmpty(m_ExportPath))GUILayout.Label("Exported: "+m_ExportPath);
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        static string LocalIpv4(){try{var host=Dns.GetHostEntry(Dns.GetHostName());foreach(var ip in host.AddressList)if(ip.AddressFamily==AddressFamily.InterNetwork)return ip.ToString();}catch(SocketException){}return "Unknown";}
    }
}
