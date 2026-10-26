using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace QuestPianoMotion.Research.Distributed
{
    public sealed partial class DistributedQuestUi
    {
        readonly Dictionary<GameObject, int> m_DetailItems = new Dictionary<GameObject, int>();
        readonly List<GameObject> m_Tabs = new List<GameObject>();
        TMP_Text m_Overview, m_PlacementStatus;
        RectTransform m_Handle;
        bool m_Details, m_Compact;
        int m_Page;
        public QuestFloatingPanel FloatingPanel { get; private set; }
        public bool DetailsVisible => m_Details;
        public bool CompactMode => m_Compact;
        public void ShowDetails(bool visible, int page = 0)
        {
            if (Safety != null && !Safety.CanUseControls) return;
            CancelIpKeyboard();
            m_Details = visible; m_Page = page; if (visible) m_Compact = false;
            ApplyLayout();
        }
        public void SetCompact(bool compact)
        {
            if (Safety != null && !Safety.CanUseControls) return;
            CancelIpKeyboard(); m_Compact = compact; m_Details = false; ApplyLayout();
        }
        Transform Child(string name)
        {
            for(var i=0;i<transform.childCount;i++)
                if(transform.GetChild(i).name==name)return transform.GetChild(i);
            return null;
        }
        void Put(string name, int page, float x, float y, float width, float height)
        {
            var child = Child(name);
            if (child == null) return;
            var rect = child as RectTransform;
            rect.anchoredPosition3D = new Vector3(x, y, child.GetComponent<TMP_Text>() != null ? -2 : 0);
            rect.sizeDelta = new Vector2(width, height);
            m_DetailItems[child.gameObject] = page;
        }
        public void RegisterBlePanel(RectTransform panel)
        {
            panel.anchoredPosition = new Vector2(20, -530);
            panel.sizeDelta = new Vector2(680, 650);
            m_DetailItems[panel.gameObject] = 1; ApplyLayout();
        }
        void BuildFloatingLayout()
        {
            FloatingPanel = gameObject.AddComponent<QuestFloatingPanel>();
            FloatingPanel.Initialize(this);
            m_Overview = CreateText("Performance Overview", transform, "実験の準備状況を確認しています", new Vector2(20,-20), new Vector2(680,150),26,TextAlignmentOptions.TopLeft,Color.white);
            var correction = m_CorrectionStatus.rectTransform;
            correction.anchoredPosition3D = new Vector3(20,-190,-2); correction.sizeDelta = new Vector2(680,90);
            m_CorrectionStatus.fontSize = 18;
            var safety = Child("Performance UI State") as RectTransform;
            safety.anchoredPosition = new Vector2(20,-290); safety.sizeDelta = new Vector2(680,38);
            CreateButton("DETAIL SETTINGS",new Vector2(20,-340),210,()=>ShowDetails(!m_Details));
            CreateButton("COMPACT / NORMAL",new Vector2(250,-340),220,()=>SetCompact(!m_Compact));
            var recenter = Child("RECENTER UI") as RectTransform;
            recenter.anchoredPosition = new Vector2(490,-340); recenter.sizeDelta = new Vector2(210,48);
            Put("START RAW REC",-1,20,-405,210,56);
            Put("STOP RAW REC",-1,250,-405,210,56);
            // A second toggle calls the same settings object; mode changes never initialize it.
            CreateButton("TIP ON / OFF",new Vector2(490,-405),210,()=>{ if(m_Client!=null)m_Client.CorrectionSettings.enabled=!m_Client.CorrectionSettings.enabled; });
            var names = new[] { "PC / CALIBRATION", "BLE MIDI", "CORRECTION", "LOG / DIAGNOSTICS", "UI POSITION" };
            for(var i=0;i<names.Length;i++)
            {
                var page=i;
                var tab=CreateButton(names[i],new Vector2(20+i*138,-475),132,()=>ShowDetails(true,page));
                tab.fontSize=18; m_Tabs.Add(tab.transform.parent.gameObject);
            }
            Put("PC IP",0,20,-540,350,56); Put("CONNECT",0,390,-540,145,56); Put("DISCONNECT",0,550,-540,150,56);
            Put("CALIBRATION A",0,20,-620,210,56); Put("CALIBRATION B",0,250,-620,210,56); Put("CALIBRATION C",0,490,-620,210,56);
            Put("SAVE CALIBRATION",0,20,-700,320,56); Put("CANCEL CAPTURE",0,360,-700,340,56);
            Put("Calibration Capture Status",0,20,-785,680,290);
            MakeScrollable(m_CalibrationStatus.rectTransform);
            m_DetailItems.Remove(m_CalibrationStatus.gameObject);
            m_DetailItems[m_CalibrationStatus.transform.parent.gameObject]=0;
            Put("KEYBOARD: 88 KEYS",0,20,-1090,250,56);
            Child("MIDI Tip Correction Panel").gameObject.SetActive(false);
            var controls=new[] { "CORRECTION ON/OFF","LEFT / RIGHT","NEXT FINGER","HAND MESH","STRENGTH -0.25","STRENGTH +0.25","MAX -5 mm","MAX +5 mm","PAD Y -1 mm","PAD Y +1 mm","Y BAND -1 mm","Y BAND +1 mm" };
            for(var i=0;i<controls.Length;i++)Put(controls[i],2,20+(i%2)*350,-540-(i/2)*75,330,60);
            Put("Correction Instructions",2,20,-1010,680,145);
            BuildRuleControls();
            Put("Status",3,20,-540,680,430);
            MakeScrollable(m_Status.rectTransform);
            m_DetailItems.Remove(m_Status.gameObject);
            m_DetailItems[m_Status.transform.parent.gameObject]=3;
            Put("Hand Input Diagnostic",3,20,-985,680,180);
            Put("HAND GAMEOBJECTS",3,20,-1180,320,56); Put("HAND GPU",3,360,-1180,340,56);
            var actions = new[] { "FACE USER", "RESET PLACEMENT", "NEARER 10 cm", "FARTHER 10 cm", "LIMITS -10 cm", "LIMITS +10 cm" };
            UnityEngine.Events.UnityAction[] calls={ ()=>FloatingPanel.FaceUser(), ()=>FloatingPanel.ResetPlacement(), ()=>FloatingPanel.AdjustDistance(-.1f), ()=>FloatingPanel.AdjustDistance(.1f), ()=>FloatingPanel.AdjustLimits(-.1f), ()=>FloatingPanel.AdjustLimits(.1f) };
            for(var i=0;i<actions.Length;i++)
            {
                CreateButton(actions[i],new Vector2(20+(i%2)*350,-540-(i/2)*85),330,calls[i],i==1);
                Put(actions[i],4,20+(i%2)*350,-540-(i/2)*85,330,64);
            }
            m_PlacementStatus=CreateText("Placement details",transform,"",new Vector2(20,-835),new Vector2(680,270),24,TextAlignmentOptions.TopLeft,Color.white);
            m_DetailItems[m_PlacementStatus.gameObject]=4;
            var handle=new GameObject("Move Panel Handle",typeof(RectTransform),typeof(Image),typeof(QuestPanelMoveHandle));
            handle.layer=gameObject.layer; handle.transform.SetParent(transform,false);
            m_Handle=(RectTransform)handle.transform;
            m_Handle.anchorMin=m_Handle.anchorMax=new Vector2(.5f,0);m_Handle.pivot=new Vector2(.5f,0);
            m_Handle.anchoredPosition3D=new Vector3(0,12,-4);m_Handle.sizeDelta=new Vector2(430,48);
            handle.GetComponent<Image>().color=new Color(.18f,.25f,.34f);handle.GetComponent<Image>().raycastTarget=true;
            handle.GetComponent<QuestPanelMoveHandle>().Panel=FloatingPanel;
            var label=CreateText("Move handle label",handle.transform,"Ray + Pinchでつかんで移動",Vector2.zero,m_Handle.sizeDelta,21,TextAlignmentOptions.Center,Color.white);
            Stretch(label.rectTransform,4,4,2,2,-2);
            ApplyLayout();
        }
        public void ApplyLayout()
        {
            if(m_Handle==null)return;
            var panelRect=(RectTransform)transform;
            var oldHeight=panelRect.sizeDelta.y;
            var newHeight=m_Compact?375:m_Details?1320:590;
            panelRect.sizeDelta=new Vector2(720,newHeight);
            // Keep the header fixed in world space when expanding details downwards.
            transform.position+=transform.up*((oldHeight-newHeight)*panelRect.lossyScale.y*.5f);
            foreach(var pair in m_DetailItems)
                if(pair.Key!=null)pair.Key.SetActive(pair.Value<0?!m_Compact:m_Details&&pair.Value==m_Page);
            foreach(var tab in m_Tabs)tab.SetActive(m_Details);
            m_HandInputDiagnostic.gameObject.SetActive(m_Details && m_Page == 3 && m_LegacyRaycaster.enabled);
            var toggle=Child("TIP ON / OFF");if(toggle!=null)toggle.gameObject.SetActive(!m_Compact);
            m_CorrectionStatus.gameObject.SetActive(m_Details && m_Page == 2);
            // Header and recovery controls remain available in compact mode.
            if(m_Compact)
            {
                (Child("Performance UI State") as RectTransform).anchoredPosition=new Vector2(20,-200);
                foreach(var name in new[]{"DETAIL SETTINGS","COMPACT / NORMAL","RECENTER UI"})
                    (Child(name) as RectTransform).anchoredPosition=new Vector2((Child(name) as RectTransform).anchoredPosition.x,-250);
                m_Overview.rectTransform.sizeDelta=new Vector2(680,175);
            }
            else
            {
                (Child("Performance UI State") as RectTransform).anchoredPosition=new Vector2(20,-290);
                foreach(var name in new[]{"DETAIL SETTINGS","COMPACT / NORMAL","RECENTER UI"})
                    (Child(name) as RectTransform).anchoredPosition=new Vector2((Child(name) as RectTransform).anchoredPosition.x,-340);
                m_Overview.rectTransform.sizeDelta=new Vector2(680,165);
            }
        }
        void UpdateOverview()
        {
            UpdateRuleDetails();
            if(m_Client==null||m_Overview==null)return;
            var ble=FindAnyObjectByType<BleMidiInput>();
            var midi=ble!=null&&ble.IsConnected?"BLE接続中":m_Client.PcConnected?"PC経由MIDI接続中":"未接続";
            var error=!string.IsNullOrEmpty(m_Client.ResearchLogError)?" / 記録エラー":ble!=null&&!string.IsNullOrEmpty(ble.LogError)?" / BLEエラー":"";
            var c=m_Client.CorrectionSettings;var method=m_Client.RuleSettings.method;
            var methodName=method==HandCorrectionMethod.Legacy?"旧方式":method==HandCorrectionMethod.Raw?"補正なし":"新方式";
            var fingers=new[]{"親指","人差し指","中指","薬指","小指"};
            var finger=fingers[Mathf.Clamp((int)c.finger,0,4)];
            m_Overview.text=$"MIDI：{midi}{error}\n鍵盤：{(m_Calibration!=null&&m_Calibration.Current!=null&&m_Calibration.Current.valid?"位置合わせ済み":"位置合わせが必要です")}　 記録：{(m_Client.ResearchRecording?"記録中":"停止中")}\n指先補正：{(c.enabled&&method!=HandCorrectionMethod.Raw?"ON":"OFF")}（{methodName}）　使用する手：{(c.leftHand?"左手":"右手")}　指：{finger}";
            if(!m_Compact&&m_Details&&m_Page==2)m_Overview.text+=$"\n補正の強さ：{c.strength:F2}　最大補正距離：{c.maxCorrectionMeters*1000:F0} mm";
            if(m_PlacementStatus!=null)m_PlacementStatus.text=$"{FloatingPanel.PersistenceStatus}\n距離範囲：{FloatingPanel.MinimumDistance:F2} ～ {FloatingPanel.MaximumDistance:F2} m\nハンドルをRay + Pinchでつかんで移動できます。\nQuestの再センター後はUIを正面に戻せます。";
        }
        // Standard ScrollRect and mask retain the complete diagnostic message at a readable font size.
        public static void MakeScrollable(RectTransform text)
        {
            var parent=text.parent;
            var viewport=new GameObject(text.name+" Scroll View",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
            viewport.layer=text.gameObject.layer;var rect=(RectTransform)viewport.transform;rect.SetParent(parent,false);
            rect.anchorMin=text.anchorMin;rect.anchorMax=text.anchorMax;rect.pivot=text.pivot;
            rect.anchoredPosition3D=text.anchoredPosition3D;rect.sizeDelta=text.sizeDelta;
            viewport.GetComponent<Image>().color=new Color(.04f,.05f,.07f,1);
            text.SetParent(rect,false);text.anchorMin=new Vector2(0,1);text.anchorMax=new Vector2(1,1);text.pivot=new Vector2(.5f,1);
            text.sizeDelta=new Vector2(0,rect.rect.height);text.anchoredPosition3D=new Vector3(0,0,-2);
            var tmp=text.GetComponent<TMP_Text>();tmp.overflowMode=TextOverflowModes.Overflow;
            var fitter=text.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=viewport.GetComponent<ScrollRect>();scroll.viewport=rect;scroll.content=text;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
        }
    }
}
