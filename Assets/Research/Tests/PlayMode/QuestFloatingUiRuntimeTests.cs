using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
namespace QuestPianoMotion.Research.Tests
{
    public sealed class QuestFloatingUiRuntimeTests
    {
        GameObject m_Go,m_CameraGo,m_Events;
        DistributedQuestUi m_Ui;
        byte[] m_PreviousPlacement;
        string m_PlacementPath;
        QuestFloatingPanel Panel=>m_Ui.FloatingPanel;
        [UnitySetUp]public IEnumerator SetUp()
        {
            m_CameraGo=new GameObject("Floating UI test camera",typeof(Camera));
            m_Events=new GameObject("Floating UI test events",typeof(EventSystem));
            m_Go=new GameObject("Floating UI test",typeof(RectTransform),typeof(DistributedQuestUi));
            yield return null;
            m_Ui=m_Go.GetComponent<DistributedQuestUi>();
            m_PlacementPath=m_Ui.FloatingPanel.SavePath;
            m_PreviousPlacement=System.IO.File.Exists(m_PlacementPath)?System.IO.File.ReadAllBytes(m_PlacementPath):null;
            m_Ui.PlaceAtCamera(m_CameraGo.GetComponent<Camera>());
        }
        [UnityTearDown]public IEnumerator TearDown(){Object.Destroy(m_Go);Object.Destroy(m_CameraGo);Object.Destroy(m_Events);yield return null;
            if(m_PreviousPlacement!=null) {System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(m_PlacementPath));System.IO.File.WriteAllBytes(m_PlacementPath,m_PreviousPlacement);}
            else if(System.IO.File.Exists(m_PlacementPath))System.IO.File.Delete(m_PlacementPath);}
        TrackedDeviceEventData Event(Vector3 origin,int id=17)
        {
            var point=m_Go.transform.Find("Move Panel Handle").position;
            var e=new TrackedDeviceEventData(m_Events.GetComponent<EventSystem>()){pointerId=id,rayPoints=new List<Vector3>{origin,point}};
            e.pointerCurrentRaycast=new RaycastResult{gameObject=m_Go.transform.Find("Move Panel Handle").gameObject,worldPosition=point};return e;
        }
        [UnityTest]public IEnumerator HandleEventsMoveWholePanelWithoutJumpAndStopOnRelease()
        {
            var handle=m_Go.transform.Find("Move Panel Handle").gameObject;
            var start=m_Go.transform.position;var rotation=m_Go.transform.rotation;
            var e=Event(Vector3.zero);var direction=(e.rayPoints[1]-e.rayPoints[0]).normalized;
            ExecuteEvents.Execute(handle,e,ExecuteEvents.pointerDownHandler);
            Assert.That(Panel.Moving,Is.True);Assert.That(m_Go.transform.position,Is.EqualTo(start));
            e.rayPoints=new List<Vector3>{Vector3.right*.15f,Vector3.right*.15f+direction*2};
            ExecuteEvents.Execute(handle,e,ExecuteEvents.dragHandler);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(m_Go.transform.position.x,Is.GreaterThan(start.x+.1f));
            Assert.That(Quaternion.Angle(rotation,m_Go.transform.rotation),Is.LessThan(.001f));
            Assert.That(m_Ui.WorldCanvas.transform,Is.SameAs(m_Go.transform));
            ExecuteEvents.Execute(handle,e,ExecuteEvents.pointerUpHandler);
            var stopped=m_Go.transform.position;yield return new WaitForSecondsRealtime(.1f);
            Assert.That(m_Go.transform.position,Is.EqualTo(stopped));Assert.That(Panel.Moving,Is.False);
            Assert.That(m_Events.GetComponent<EventSystem>().currentSelectedGameObject,Is.Null);
            // Move again after release at the new world hit position.
            ExecuteEvents.Execute(handle,Event(Vector3.zero),ExecuteEvents.pointerDownHandler);Assert.That(Panel.Moving,Is.True);
        }
        [Test]public void OnlyHandleReceivesDragAndModalBlocksIt()
        {
            foreach(var button in m_Go.GetComponentsInChildren<Button>(true))
                Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(button.gameObject),Is.Null,button.name);
            m_Ui.Safety.Run("Confirm",()=>{},true);
            var e=Event(Vector3.zero);ExecuteEvents.Execute(m_Go.transform.Find("Move Panel Handle").gameObject,e,ExecuteEvents.pointerDownHandler);
            Assert.That(Panel.Moving,Is.False);
        }
        [UnityTest]public IEnumerator MidiLockCancelsMove()
        {
            var e=Event(Vector3.zero);ExecuteEvents.Execute(m_Go.transform.Find("Move Panel Handle").gameObject,e,ExecuteEvents.pointerDownHandler);
            typeof(PerformanceUiSafety).GetMethod("SetLocked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(m_Ui.Safety,new object[]{true});
            Panel.Move(new Ray(Vector3.zero,Vector3.forward),17);
            Assert.That(Panel.Moving,Is.False);yield return null;
        }
        [Test]public void FrontFaceResetAndDistanceControlsKeepUprightAndWithinLimits()
        {
            m_CameraGo.transform.rotation=Quaternion.Euler(75,60,30);m_Ui.RecenterUi();
            Assert.That(Vector3.Dot(m_Go.transform.up,Vector3.up),Is.GreaterThan(.999f));
            var before=m_Go.transform.position;Panel.FaceUser();Assert.That(m_Go.transform.position,Is.EqualTo(before));
            Panel.AdjustDistance(100);Assert.That(Vector3.Distance(m_Go.transform.position,m_CameraGo.transform.position),Is.EqualTo(Panel.MaximumDistance).Within(.001f));
            Panel.AdjustDistance(-100);Assert.That(Vector3.Distance(m_Go.transform.position,m_CameraGo.transform.position),Is.EqualTo(Panel.MinimumDistance).Within(.001f));
            Panel.ResetPlacement();Assert.That(Vector3.Distance(m_Go.transform.position,m_CameraGo.transform.position),Is.InRange(.5f,1.5f));
        }
        [UnityTest]public IEnumerator EveryPageHasNonOverlappingButtonsAndReadableTmp()
        {
            for(var page=0;page<5;page++)
            {
                m_Ui.ShowDetails(true,page);Canvas.ForceUpdateCanvases();yield return null;
                var buttons=m_Go.GetComponentsInChildren<Button>().Select(x=>(RectTransform)x.transform).ToArray();
                for(var i=0;i<buttons.Length;i++)for(var j=i+1;j<buttons.Length;j++)
                    Assert.That(Bounds(buttons[i]).Overlaps(Bounds(buttons[j])),Is.False,buttons[i].name+" overlaps "+buttons[j].name);
                foreach(var text in m_Go.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.fontSize,Is.GreaterThanOrEqualTo(18),text.name);
                    if(text.transform.parent.GetComponent<Button>()!=null)
                        Assert.That(text.isTextTruncated,Is.False,text.text);
                }
                CapturePreview("details-"+page);
            }
            m_Ui.ShowDetails(false);CapturePreview("normal");
            m_Ui.SetCompact(true);CapturePreview("compact");Assert.That(m_Ui.CompactMode,Is.True);Assert.That(m_Ui.DetailsVisible,Is.False);
            Assert.That(m_Go.transform.Find("Performance Overview").gameObject.activeInHierarchy,Is.True);
            var handle=(RectTransform)m_Go.transform.Find("Move Panel Handle");
            foreach(var button in m_Go.GetComponentsInChildren<Button>())
                Assert.That(Bounds(handle).Overlaps(Bounds((RectTransform)button.transform)),Is.False,"Compact handle overlaps "+button.name);
        }
        void CapturePreview(string name)
        {
            var folder=System.Environment.GetEnvironmentVariable("QUEST_UI_CAPTURE_DIR");
            if(string.IsNullOrEmpty(folder)||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            Canvas.ForceUpdateCanvases();
            var camera=m_CameraGo.GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(m_Go.transform.position-m_Go.transform.forward*2,m_Go.transform.rotation);
            camera.orthographic=true;camera.aspect=1;camera.orthographicSize=Mathf.Max(((RectTransform)m_Go.transform).rect.height,720)*.00065f*.55f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.1f,.12f);
            var rt=new RenderTexture(1600,1600,24);var texture=new Texture2D(1600,1600,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1600,1600),0,0);texture.Apply();
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),texture.EncodeToPNG());
            }
            finally{camera.targetTexture=null;RenderTexture.active=previous;Object.Destroy(rt);Object.Destroy(texture);}
        }
        [UnityTest]public IEnumerator JapaneseAndEnglishDiagnosticsGenerateActualGlyphsInsideScrollViewport()
        {
            m_Ui.ShowDetails(true,3);
            const string message="鍵盤較正が未完了です。右手人差し指を確認してください。 MIDI disconnected / RAW recording error.";
            m_Ui.StatusText.text=string.Join("\n",Enumerable.Repeat(message,12));
            Canvas.ForceUpdateCanvases();yield return null;m_Ui.StatusText.ForceMeshUpdate();
            foreach(var character in m_Ui.StatusText.textInfo.characterInfo)
                if(character.isVisible)Assert.That(character.textElement.unicode,Is.Not.EqualTo(0x25A1),"Missing glyph: "+character.character);
            Assert.That(m_Ui.FontAsset.fallbackFontAssetTable.Count,Is.GreaterThan(0));
            Assert.That(m_Ui.FontAsset.fallbackFontAssetTable[0].HasCharacter('鍵',true,true),Is.True);
            CapturePreview("japanese-diagnostics");
        }
        Rect Bounds(RectTransform r)
        {
            var corners=new Vector3[4];r.GetWorldCorners(corners);
            var a=m_Go.transform.InverseTransformPoint(corners[0]);var b=m_Go.transform.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        [UnityTest]public IEnumerator LongDiagnosticsScrollWithoutExpandingViewportOrOverlappingButtons()
        {
            m_Ui.ShowDetails(true,3);var viewport=m_Ui.StatusText.transform.parent.GetComponent<RectTransform>();var size=viewport.sizeDelta;
            m_Ui.StatusText.text=string.Join("\n",Enumerable.Repeat(new string('W',120),50));
            Canvas.ForceUpdateCanvases();yield return null;
            Assert.That(viewport.sizeDelta,Is.EqualTo(size));
            Assert.That(m_Ui.StatusText.rectTransform.rect.height,Is.GreaterThan(viewport.rect.height));
            Assert.That(viewport.GetComponent<RectMask2D>(),Is.Not.Null);
            Assert.That(viewport.GetComponent<ScrollRect>().horizontal,Is.False);
            Assert.That(viewport.GetComponent<QuestPanelMoveHandle>(),Is.Null);
        }
    }
}
