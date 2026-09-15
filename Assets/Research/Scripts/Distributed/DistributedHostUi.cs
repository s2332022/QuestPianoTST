using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedHostUi : MonoBehaviour
    {
        DistributedPcHost m_Host;Vector2 m_Scroll;string m_ExportPath="",m_LocalIp="Unknown";
        void Start(){m_Host=FindFirstObjectByType<DistributedPcHost>();m_LocalIp=LocalIpv4();}
        void OnGUI()
        {
            if(m_Host==null)return;GUILayout.BeginArea(new Rect(12,12,520,Screen.height-24),GUI.skin.box);m_Scroll=GUILayout.BeginScrollView(m_Scroll);
            GUILayout.Label("Quest Piano Motion - Distributed PC Host");GUILayout.Label("Build: "+m_Host.BuildIdentifier);GUILayout.Label("PC IP: "+m_LocalIp);GUILayout.Label("Bind: "+m_Host.BindAddress);GUILayout.Label("Pose receive port: "+m_Host.Settings.pcReceivePort);GUILayout.Label("MIDI destination port: "+m_Host.Settings.questReceivePort);GUILayout.Label("Clock Sync port: "+m_Host.Settings.clockSyncPort);GUILayout.Label("Windows Firewall: allow this app on Private networks (not modified automatically).");GUILayout.Label("Quest IP: "+m_Host.QuestIpAddress);GUILayout.Label("Quest Connected: "+m_Host.QuestConnected);GUILayout.Label("Connected at: "+m_Host.ConnectedAt.ToString("F3"));GUILayout.Label("Last received: "+m_Host.LastReceivedTimestamp.ToString("F3")+" s (age "+m_Host.LastReceivedAge.ToString("F2")+" s)");GUILayout.Label("Received packets: "+m_Host.ReceivedPacketCount);GUILayout.Label("Sequence missing: "+m_Host.MissingPacketCount+" (Pose "+m_Host.PoseMissingPacketCount+")");GUILayout.Label("Clock Offset: "+m_Host.ClockOffset.ToString("F6")+" s");GUILayout.Label("RTT: "+(m_Host.Rtt*1000d).ToString("F2")+" ms");GUILayout.Label("Clock Samples: "+m_Host.ClockSamples);GUILayout.Label("Pose receive rate: "+m_Host.PoseReceiveRate.ToString("F1")+" packet/s");GUILayout.Label("Pose packet loss estimate: "+(m_Host.PoseLossEstimate*100d).ToString("F2")+" %");
            var midi=m_Host.Midi;GUILayout.Label("MIDI Device: "+midi.ConnectedDeviceName);GUILayout.Label("MIDI Connected: "+midi.IsConnected);GUILayout.Label("Last MIDI Event: "+midi.LastEventText);GUILayout.Label("Session State: "+m_Host.SessionState);GUILayout.Label("Recording Time: "+(m_Host.Recorder?.RecordingSeconds??0d).ToString("F1")+" s");GUILayout.Label("Save Path: "+m_Host.SavePath);
            if(GUILayout.Button("Refresh MIDI Devices"))midi.RefreshDeviceList();for(var i=0;i<midi.Devices.Count;++i)if(GUILayout.Button("Select: "+midi.Devices[i].Name))midi.SelectDevice(i);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Connect MIDI"))midi.ConnectSelectedDevice();if(GUILayout.Button("Disconnect MIDI"))midi.Disconnect();GUILayout.EndHorizontal();GUILayout.BeginHorizontal();if(GUILayout.Button("Start Network"))m_Host.StartNetwork();if(GUILayout.Button("Stop Network"))m_Host.StopNetwork();GUILayout.EndHorizontal();GUILayout.BeginHorizontal();if(GUILayout.Button("Start Session"))m_Host.StartSession();if(GUILayout.Button("Stop Session"))m_Host.StopSession();GUILayout.EndHorizontal();if(GUILayout.Button("Export Diagnostics"))m_ExportPath=m_Host.ExportDiagnostics();if(!string.IsNullOrEmpty(m_ExportPath))GUILayout.Label("Exported: "+m_ExportPath);
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        static string LocalIpv4()
        {
            try
            {
                string fallback=null;
                foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if(adapter.OperationalStatus!=OperationalStatus.Up||adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback||adapter.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                    var properties=adapter.GetIPProperties();var hasIpv4Gateway=false;
                    foreach(var gateway in properties.GatewayAddresses)if(gateway.Address.AddressFamily==AddressFamily.InterNetwork&&!gateway.Address.Equals(IPAddress.Any)){hasIpv4Gateway=true;break;}
                    foreach(var address in properties.UnicastAddresses)if(address.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(address.Address)){var value=address.Address.ToString();if(hasIpv4Gateway)return value;if(fallback==null)fallback=value;}
                }
                if(fallback!=null)return fallback;
            }
            catch{}
            return "Unknown";
        }
    }
}
