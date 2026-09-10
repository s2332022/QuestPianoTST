using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    public enum PianoKeyVisualState { Rest, Pressed }

    public sealed class PianoKeyView
    {
        readonly Transform m_Transform;
        readonly Renderer m_Renderer;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        readonly Color m_RestColor;
        public readonly int MidiNoteNumber;
        public readonly bool IsBlack;
        public readonly Vector3 RestLocalPosition;
        public readonly Vector3 PressedLocalPosition;
        public bool Pressed { get; private set; }
        public int Velocity { get; private set; }
        public PianoKeyVisualState VisualState { get; private set; }

        public PianoKeyView(int note, bool black, Transform transform, Renderer renderer, Color restColor)
        {
            MidiNoteNumber = note;
            IsBlack = black;
            m_Transform = transform;
            m_Renderer = renderer;
            m_RestColor = restColor;
            RestLocalPosition = transform.localPosition;
            PressedLocalPosition = RestLocalPosition + Vector3.down * 0.008f;
            Apply(false, 0);
        }

        public void Apply(bool pressed, int velocity)
        {
            Pressed = pressed;
            Velocity = pressed ? Mathf.Clamp(velocity, 0, 127) : 0;
            VisualState = pressed ? PianoKeyVisualState.Pressed : PianoKeyVisualState.Rest;
            m_Transform.localPosition = pressed ? PressedLocalPosition : RestLocalPosition;
            var amount = Velocity / 127f;
            var color = pressed ? Color.Lerp(m_RestColor, new Color(0.1f, 0.7f, 1f), 0.35f + amount * 0.65f) : m_RestColor;
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            m_Block.SetColor("_EmissionColor", pressed ? color * (0.2f + amount * 1.8f) : Color.black);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }

    [DisallowMultipleComponent]
    public sealed class VirtualPianoKeyboard : MonoBehaviour
    {
        public const int FirstNote = 60;
        public const int LastNote = 72;
        readonly Dictionary<int, PianoKeyView> m_Keys = new Dictionary<int, PianoKeyView>(13);
        readonly KeyboardStateTracker m_State = new KeyboardStateTracker();
        Material m_SharedMaterial;

        public event Action<KeyboardStateChange> StateChanged;
        public KeyboardStateTracker State => m_State;
        public Transform KeyboardRoot { get; private set; }

        void Awake()
        {
            BuildKeyboard();
            m_State.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            m_State.StateChanged -= OnStateChanged;
            if (m_SharedMaterial != null) Destroy(m_SharedMaterial);
        }

        public void ApplyMidi(in MidiMessage message) => m_State.Apply(in message);

        public void ApplyCalibration(PianoCalibrationData calibration)
        {
            if (calibration == null || !calibration.valid || KeyboardRoot == null) return;
            KeyboardRoot.SetPositionAndRotation(calibration.origin, calibration.rotation);
        }

        void OnStateChanged(KeyboardStateChange change)
        {
            if (m_Keys.TryGetValue(change.NoteNumber, out var key))
                key.Apply(change.Pressed, change.Velocity);
            StateChanged?.Invoke(change);
        }

        void BuildKeyboard()
        {
            KeyboardRoot = new GameObject("Virtual Piano Keyboard C4-C5").transform;
            KeyboardRoot.SetParent(transform, false);
            KeyboardRoot.localPosition = new Vector3(-0.15f, -0.35f, 0.65f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m_SharedMaterial = new Material(shader) { enableInstancing = true };
            const float whiteWidth = 0.036f;
            const float whiteDepth = 0.16f;
            var whiteIndex = 0;
            for (var note = FirstNote; note <= LastNote; ++note)
            {
                var black = IsBlack(note);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Key {note} {(black ? "Black" : "White")}";
                go.transform.SetParent(KeyboardRoot, false);
                var x = black ? (whiteIndex - 0.5f) * whiteWidth : whiteIndex * whiteWidth;
                var y = black ? 0.012f : 0f;
                var z = black ? 0.035f : 0f;
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localScale = black
                    ? new Vector3(whiteWidth * 0.58f, 0.025f, whiteDepth * 0.58f)
                    : new Vector3(whiteWidth * 0.94f, 0.018f, whiteDepth);
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = m_SharedMaterial;
                var color = black ? new Color(0.025f, 0.025f, 0.03f) : new Color(0.88f, 0.88f, 0.84f);
                m_Keys.Add(note, new PianoKeyView(note, black, go.transform, renderer, color));
                if (!black) ++whiteIndex;
            }
        }

        static bool IsBlack(int note)
        {
            var pitch = note % 12;
            return pitch == 1 || pitch == 3 || pitch == 6 || pitch == 8 || pitch == 10;
        }
    }
}
