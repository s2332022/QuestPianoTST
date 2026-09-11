using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class PianoMinimalRuntimeTests
    {
        [UnityTest]
        public IEnumerator MinimalScene_StartsWithoutMidiAndBuildsOnlyRequiredRuntime()
        {
            var origin = new GameObject("XR Origin");
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(origin.transform, false);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            var pianoRoot = new GameObject("Piano Root");
            var uiRoot = new GameObject("Research UI");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(MinimalHandUiInputModule));
            var runtimeObject = new GameObject("Research Runtime");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<PianoResearchRuntime>();
            runtime.ConfigureMinimalScene(pianoRoot.transform, uiRoot.transform);
            runtimeObject.SetActive(true);

            yield return null;
            yield return null;

            Assert.That(runtime, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<PianoResearchRuntime>().Length, Is.EqualTo(1));
            Assert.That(runtime.GetComponent<XRHandPoseProvider>(), Is.Not.Null);
            Assert.That(runtime.GetComponent<AndroidMidiInput>(), Is.Not.Null);
            Assert.That(runtime.GetComponent<PianoCalibrationManager>(), Is.Not.Null);
            var recorder = runtime.GetComponent<SynchronizedSessionRecorder>();
            Assert.That(recorder, Is.Not.Null);
            Assert.That(runtime.GetComponent<MinimalHandVisualizer>(), Is.Not.Null);

            var keyboard = runtime.GetComponent<VirtualPianoKeyboard>();
            Assert.That(keyboard, Is.Not.Null);
            Assert.That(keyboard.KeyboardRoot.parent.name, Is.EqualTo("Piano Root"));
            Assert.That(keyboard.KeyboardRoot.childCount, Is.EqualTo(13));
            Assert.That(keyboard.KeyboardRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(keyboard.KeyboardRoot.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(keyboard.KeyboardRoot.GetComponentsInChildren<AudioSource>(true), Is.Empty);

            var canvas = GameObject.Find("Research UI").GetComponentInChildren<Canvas>(true);
            Assert.That(canvas, Is.Not.Null);
            var status = canvas.GetComponentsInChildren<Text>(true).Single(text => text.name == "Status");
            Assert.That(status.text.Split('\n').Length, Is.EqualTo(7));
            var expectedButtons = new[]
            {
                "Refresh MIDI", "Connect MIDI", "Point Hand Left", "Point Hand Right",
                "Capture A", "Capture B", "Capture C", "Save Calibration", "Load Calibration",
                "Start Recording", "Stop Recording"
            };
            var actualButtons = canvas.GetComponentsInChildren<Button>(true).Select(button => button.name).ToArray();
            CollectionAssert.AreEquivalent(expectedButtons, actualButtons);

            Assert.That(recorder.StartRecording(), Is.True);
            yield return null;
            Assert.That(recorder.IsRecording, Is.True);
            recorder.StopRecording();
            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(Directory.GetFiles(recorder.SessionPath).Length, Is.EqualTo(7));

            Object.Destroy(runtimeObject);
            Object.Destroy(eventSystem);
            Object.Destroy(uiRoot);
            Object.Destroy(pianoRoot);
            Object.Destroy(origin);
            yield return null;
        }
    }
}
