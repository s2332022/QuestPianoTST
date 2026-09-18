using System.Collections;
using System.IO;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class QuestIpInputRuntimeTests
    {
        GameObject m_SettingsObject;
        GameObject m_UiObject;
        GameObject m_EventSystemObject;
        DistributedSettings m_Settings;
        DistributedQuestUi m_Ui;
        byte[] m_PreviousSavedIp;
        string m_SavedIpPath;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SavedIpPath = Path.Combine(Application.persistentDataPath, "PianoResearch", "distributed_host_ip.json");
            if (File.Exists(m_SavedIpPath))
                m_PreviousSavedIp = File.ReadAllBytes(m_SavedIpPath);

            m_SettingsObject = new GameObject("IP Test Settings");
            m_Settings = m_SettingsObject.AddComponent<DistributedSettings>();
            m_Settings.pcIpAddress = "192.168.1.2";
            m_EventSystemObject = new GameObject("IP Test EventSystem", typeof(EventSystem));
            m_UiObject = new GameObject("IP Test UI", typeof(RectTransform), typeof(DistributedQuestUi));
            m_Ui = m_UiObject.GetComponent<DistributedQuestUi>();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_PreviousSavedIp != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_SavedIpPath));
                File.WriteAllBytes(m_SavedIpPath, m_PreviousSavedIp);
            }
            else if (File.Exists(m_SavedIpPath))
                File.Delete(m_SavedIpPath);
            Object.Destroy(m_UiObject);
            Object.Destroy(m_SettingsObject);
            Object.Destroy(m_EventSystemObject);
            yield return null;
        }

        [TestCase("10.76.247.112")]
        [TestCase("192.168.1.10")]
        [TestCase("127.0.0.1")]
        public void ValidIpv4_IsAccepted(string value) => Assert.That(DistributedQuestUi.IsValidIpv4(value), Is.True);

        [TestCase("")]
        [TestCase("10.76.247")]
        [TestCase("10.76.247.112.5")]
        [TestCase("256.1.1.1")]
        [TestCase("192.168.-1.1")]
        [TestCase("abc.def.ghi.jkl")]
        [TestCase("10..1.1")]
        [TestCase("10.1.1.")]
        public void InvalidIpv4_IsRejected(string value) => Assert.That(DistributedQuestUi.IsValidIpv4(value), Is.False);

        [UnityTest]
        public IEnumerator SelectingIpField_OpensDedicatedKeyboardAndEditsOneCharacterPerKey()
        {
            Assert.That(m_Ui.IpInput, Is.Not.Null);
            Assert.That(m_Ui.IpInput.readOnly, Is.True);
            m_Ui.OpenIpKeyboard();
            Assert.That(m_Ui.IpKeyboardVisible, Is.True);
            m_Ui.HandleIpKey("CLEAR");
            m_Ui.HandleIpKey("1");
            Assert.That(m_Ui.EditingIp, Is.EqualTo("1"));
            m_Ui.HandleIpKey("BACKSPACE");
            Assert.That(m_Ui.EditingIp, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Keyboard_UsesSingleIndependentCanvasOutsideResearchUi()
        {
            Assert.That(m_Ui.IpKeyboardCanvas, Is.Not.Null);
            Assert.That(m_Ui.IpKeyboardRaycaster, Is.Not.Null);
            Assert.That(m_Ui.IpKeyboardRaycaster.enabled, Is.True);
            Assert.That(m_Ui.IpKeyboardCanvas.transform.parent, Is.SameAs(m_Ui.WorldCanvas.transform));

            var researchRect = m_Ui.WorldCanvas.GetComponent<RectTransform>();
            var keyboardRect = m_Ui.IpKeyboardRect;
            Assert.That(keyboardRect.localPosition.x,
                Is.GreaterThan(researchRect.rect.width * 0.5f + keyboardRect.rect.width * 0.5f));
            Assert.That(keyboardRect.localPosition.z, Is.LessThan(0f));
            Assert.That(m_Ui.IpKeyboardCanvas.GetComponentsInChildren<Canvas>(true), Has.Length.EqualTo(1));
            Assert.That(m_Ui.IpKeyboardCanvas.GetComponentsInChildren<Button>(true), Has.Length.EqualTo(15));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Cancel_PreservesExistingIp_AndApplyCommitsValidIp()
        {
            m_Ui.OpenIpKeyboard();
            m_Ui.HandleIpKey("CLEAR");
            foreach (var key in new[] { "10", ".", "76", ".", "247", ".", "112" })
                foreach (var character in key)
                    m_Ui.HandleIpKey(character.ToString());
            Assert.That(m_Ui.ApplyIpKeyboardValue(), Is.True);
            Assert.That(m_Settings.pcIpAddress, Is.EqualTo("10.76.247.112"));
            Assert.That(m_Ui.IpKeyboardVisible, Is.False);

            m_Ui.OpenIpKeyboard();
            m_Ui.HandleIpKey("CLEAR");
            m_Ui.HandleIpKey("1");
            m_Ui.CancelIpKeyboard();
            Assert.That(m_Settings.pcIpAddress, Is.EqualTo("10.76.247.112"));
            Assert.That(m_Ui.IpInput.text, Is.EqualTo("10.76.247.112"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidApply_DoesNotReplaceExistingIp()
        {
            m_Ui.OpenIpKeyboard();
            m_Ui.HandleIpKey("CLEAR");
            foreach (var character in "256.1.1.1")
                m_Ui.HandleIpKey(character.ToString());
            Assert.That(m_Ui.ApplyIpKeyboardValue(), Is.False);
            Assert.That(m_Settings.pcIpAddress, Is.EqualTo("192.168.1.2"));
            Assert.That(m_Ui.IpKeyboardVisible, Is.True);
            Assert.That(m_Ui.IpValidationError, Is.Not.Empty);
            yield return null;
        }
    }
}
