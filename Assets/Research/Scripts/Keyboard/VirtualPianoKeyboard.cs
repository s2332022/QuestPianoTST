using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestPianoMotion.Research
{
    public enum PianoKeyVisualState { Rest, Pressed }
    public enum KeyboardDisplayMode { Research13Keys, Full88Keys }

    public sealed class PianoKeyView
    {
        readonly Transform m_Transform;
        readonly Renderer m_Renderer;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        readonly Color m_RestColor;
        float m_Opacity = 1f;
        public readonly int MidiNoteNumber;
        public readonly bool IsBlack;
        public readonly Vector3 RestLocalPosition;
        public readonly Vector3 PressedLocalPosition;
        public bool Pressed { get; private set; }
        public int Velocity { get; private set; }
        public PianoKeyVisualState VisualState { get; private set; }
        public Renderer Renderer => m_Renderer;
        public float Opacity => m_Opacity;

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
            color.a = m_Opacity;
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            m_Block.SetColor("_EmissionColor", pressed ? color * (0.2f + amount * 1.8f) : Color.black);
            m_Renderer.SetPropertyBlock(m_Block);
        }

        public void SetOpacity(float opacity)
        {
            m_Opacity = opacity;
            Apply(Pressed, Velocity);
        }
    }

    [DisallowMultipleComponent]
    public sealed class VirtualPianoKeyboard : MonoBehaviour
    {
        public const int FirstNote = 60;
        public const int LastNote = 72;
        public const float BaseWhiteKeyPitchMeters = 0.036f;
        public const float BaseOctaveSpanMeters = BaseWhiteKeyPitchMeters * 7f;
        public const float BaseWhiteKeyWidthMeters = 0.03384f;
        public const float BaseWhiteKeyDepthMeters = 0.160f;
        public const float BaseWhiteKeyHeightMeters = 0.018f;
        readonly Dictionary<int, PianoKeyView> m_Keys = new Dictionary<int, PianoKeyView>(88);
        readonly KeyboardStateTracker m_State = new KeyboardStateTracker();
        // Keep existing UDP state/snapshot/log semantics; merge source ownership only for views.
        readonly KeyboardStateTracker m_BleState = new KeyboardStateTracker();
        Distributed.IMidiInput m_BleInput;
        const float WhiteKeySeparatorWidthMeters = 0.0012f;
        const float WhiteKeySeparatorHeightMeters = 0.0002f;
        const float WhiteKeySeparatorCenterAboveTopMeters = 0.00025f;
        readonly List<Renderer> m_WhiteKeySeparators = new List<Renderer>(51);
        Material m_SharedMaterial;
        Material m_TransparentMaterial;
        bool m_Minimal;
        bool m_CalibrationTransparency;
        KeyboardDisplayMode m_Mode = KeyboardDisplayMode.Full88Keys;

        public event Action<KeyboardStateChange> StateChanged;
        public KeyboardStateTracker State => m_State;
        // Read-only merged physical key ownership, including notes outside the displayed range.
        public bool HasActiveMidiNotes
        {
            get
            {
                for (var note = 0; note < 128; ++note)
                    if (m_State.IsPressed(note) || m_BleState.IsPressed(note)) return true;
                return false;
            }
        }
        public KeyboardDisplayMode DisplayMode => m_Mode;
        public int MinNote => m_Mode == KeyboardDisplayMode.Research13Keys ? FirstNote : 21;
        public int MaxNote => m_Mode == KeyboardDisplayMode.Research13Keys ? LastNote : 108;
        public int KeyCount => MaxNote - MinNote + 1;
        public bool TryGetKey(int note, out PianoKeyView key) => m_Keys.TryGetValue(note, out key);
        public bool CalibrationTransparency => m_CalibrationTransparency;
        public Transform KeyboardRoot { get; private set; }
        public Transform KeyboardGeometry { get; private set; }
        public static Vector3 BaseGeometryOriginOffsetMeters =>
            new Vector3(BaseWhiteKeyWidthMeters * 0.5f, 0f, BaseWhiteKeyDepthMeters * 0.5f);

        void Awake()
        {
            BuildKeyboard();
            m_State.StateChanged += OnStateChanged;
            m_BleState.StateChanged += OnBleStateChanged;
        }

        void OnDestroy()
        {
            BindBleMidi(null);
            m_BleState.StateChanged -= OnBleStateChanged;
            m_State.StateChanged -= OnStateChanged;
            if (m_SharedMaterial != null) Destroy(m_SharedMaterial);
        }

        public void ApplyMidi(in MidiMessage message) => m_State.Apply(in message);

        public void BindBleMidi(Distributed.IMidiInput input)
        {
            if (ReferenceEquals(m_BleInput, input)) return;
            if (m_BleInput != null) m_BleInput.MessageReceived -= OnBleMidi;
            m_BleInput = input;
            m_BleState.Reset(ResearchServices.Clock.AbsoluteSeconds);
            if (m_BleInput != null) m_BleInput.MessageReceived += OnBleMidi;
        }

        void OnBleMidi(MidiMessage message)
        {
            if (message.EventType == MidiEventType.NoteOn && message.Velocity == 0 &&
                message.Channel >= 1 && message.Channel <= 16)
                message = AndroidMidiInput.NormalizeMessage(message.AbsoluteTimeSeconds, message.EventIndex,
                    message.DeviceName, 0x90 | (message.Channel - 1), message.NoteNumber, 0);
            m_BleState.Apply(in message);
        }

        void OnBleStateChanged(KeyboardStateChange change) => ApplyKeyVisual(change.NoteNumber);

        void ApplyKeyVisual(int note)
        {
            if (!m_Keys.TryGetValue(note, out var key)) return;
            var pressed = m_State.IsPressed(note) || m_BleState.IsPressed(note);
            var velocity = pressed ? Math.Max(m_State.Velocity(note), m_BleState.Velocity(note)) : 0;
            // Idempotent absolute-position animation even with simultaneous same-note inputs.
            if (key.Pressed != pressed || key.Velocity != velocity) key.Apply(pressed, velocity);
        }


        public void SetCalibrationTransparency(bool enabled)
        {
            if (m_CalibrationTransparency == enabled) return;
            if (enabled && m_TransparentMaterial == null)
                m_TransparentMaterial = Resources.Load<Material>("CalibrationKeyTransparent");
            if (enabled && m_TransparentMaterial == null)
            {
                Debug.LogError("Calibration transparent key material is missing.", this);
                return;
            }
            m_CalibrationTransparency = enabled;
            foreach (var key in m_Keys.Values)
            {
                key.Renderer.sharedMaterial = enabled ? m_TransparentMaterial : m_SharedMaterial;
                key.SetOpacity(enabled ? 0.35f : 1f);
            }
            UpdateWhiteKeySeparatorVisuals();
        }

        public void ConfigureMinimal(Transform host)
        {
            m_Minimal = true;
            if (KeyboardRoot != null && host != null)
            {
                KeyboardRoot.SetParent(host, false);
                KeyboardRoot.localPosition = Vector3.zero;
                KeyboardRoot.localRotation = Quaternion.identity;
            }

            var unlit = Resources.Load<Shader>("ResearchUnlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (unlit != null && m_SharedMaterial != null)
                m_SharedMaterial.shader = unlit;

            foreach (var key in m_Keys.Values)
            {
                var renderer = key.Renderer;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var collider = renderer.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying) Destroy(collider);
                    else DestroyImmediate(collider);
                }
            }
        }

        public void ApplyCalibration(PianoCalibrationData calibration) => TryApplyCalibration(calibration);

        public bool TryApplyCalibration(PianoCalibrationData calibration)
        {
            if (calibration == null || !calibration.valid || KeyboardRoot == null || KeyboardGeometry == null ||
                !PianoCalibrationMath.TryGetScale(calibration, out var scaleX, out var scaleZ, out _))
                return false;

            var origin = calibration.origin;
            var rotation = calibration.rotation;
            if (!PianoCalibrationMath.IsFinite(origin.x) || !PianoCalibrationMath.IsFinite(origin.y) ||
                !PianoCalibrationMath.IsFinite(origin.z) || !PianoCalibrationMath.IsFinite(rotation.x) ||
                !PianoCalibrationMath.IsFinite(rotation.y) || !PianoCalibrationMath.IsFinite(rotation.z) ||
                !PianoCalibrationMath.IsFinite(rotation.w) ||
                rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                rotation.w * rotation.w < 0.000001f)
                return false;

            KeyboardRoot.localScale = Vector3.one;
            KeyboardRoot.SetPositionAndRotation(origin, rotation);
            KeyboardGeometry.localScale = new Vector3(scaleX, 1f, scaleZ);
            KeyboardGeometry.localPosition = new Vector3(
                BaseWhiteKeyWidthMeters * scaleX * 0.5f,
                0f,
                BaseWhiteKeyDepthMeters * scaleZ * 0.5f);
            UpdateWhiteKeySeparatorVisuals();
            return true;
        }

        void OnStateChanged(KeyboardStateChange change)
        {
            ApplyKeyVisual(change.NoteNumber);
            StateChanged?.Invoke(change);
        }

        public void SetDisplayMode(KeyboardDisplayMode mode)
        {
            if (mode != KeyboardDisplayMode.Research13Keys && mode != KeyboardDisplayMode.Full88Keys)
                mode = KeyboardDisplayMode.Full88Keys;
            if (m_Mode == mode && KeyboardGeometry != null) return;
            m_Mode = mode;
            if (KeyboardGeometry != null) BuildKeyboard();
        }

        void BuildKeyboard()
        {
            if (KeyboardRoot == null)
            {
                KeyboardRoot = new GameObject("Virtual Piano Keyboard C4").transform;
                KeyboardRoot.SetParent(transform, false);
                KeyboardRoot.localPosition = Vector3.zero;
                KeyboardRoot.localScale = Vector3.one;
                KeyboardGeometry = new GameObject("Keyboard Geometry").transform;
                KeyboardGeometry.SetParent(KeyboardRoot, false);
                KeyboardGeometry.localPosition = BaseGeometryOriginOffsetMeters;
                KeyboardGeometry.localScale = Vector3.one;
            }
            else
            {
                m_Keys.Clear();
                m_WhiteKeySeparators.Clear();
                for (var i = KeyboardGeometry.childCount - 1; i >= 0; --i)
                {
                    var oldKey = KeyboardGeometry.GetChild(i).gameObject;
                    oldKey.SetActive(false);
                    oldKey.transform.SetParent(null, false);
                    if (Application.isPlaying) Destroy(oldKey);
                    else DestroyImmediate(oldKey);
                }
            }
            if (m_SharedMaterial == null)
            {
                var shader = Resources.Load<Shader>("ResearchUnlit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m_SharedMaterial = new Material(shader) { enableInstancing = true };
            }
            const float blackKeyHeightMeters = 0.025f;
            var hasPreviousWhiteKey = false;
            for (var note = MinNote; note <= MaxNote; ++note)
            {
                var black = IsBlack(note);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Key {note} {(black ? "Black" : "White")}";
                go.transform.SetParent(KeyboardGeometry, false);
                var whiteIndex = WhiteIndexFromC4(black ? note + 1 : note);
                var x = (whiteIndex - (black ? 0.5f : 0f)) * BaseWhiteKeyPitchMeters;
                var y = black ? 0.012f : -BaseWhiteKeyHeightMeters * 0.5f;
                var z = black ? 0.035f : 0f;
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localScale = black
                    ? new Vector3(BaseWhiteKeyPitchMeters * 0.58f, blackKeyHeightMeters,
                        BaseWhiteKeyDepthMeters * 0.58f)
                    : new Vector3(BaseWhiteKeyWidthMeters, BaseWhiteKeyHeightMeters,
                        BaseWhiteKeyDepthMeters);
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = m_CalibrationTransparency ? m_TransparentMaterial : m_SharedMaterial;
                if (m_Minimal)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var collider = go.GetComponent<Collider>();
                    if (Application.isPlaying) Destroy(collider);
                    else DestroyImmediate(collider);
                }
                var color = black ? new Color(0.025f, 0.025f, 0.03f) : new Color(0.88f, 0.88f, 0.84f);
                var key = new PianoKeyView(note, black, go.transform, renderer, color);
                m_Keys.Add(note, key);
                if (!black)
                {
                    if (hasPreviousWhiteKey) CreateWhiteKeySeparator(go.transform);
                    hasPreviousWhiteKey = true;
                }
                ApplyKeyVisual(note);
                if (m_CalibrationTransparency) key.SetOpacity(0.35f);
            }
            UpdateWhiteKeySeparatorVisuals();
        }

        void CreateWhiteKeySeparator(Transform whiteKey)
        {
            // KeyboardGeometry-local metres, converted into the scaled white key's local space.
            // Mesh components only: never create a Collider, interaction component or PianoKeyView.
            var separator = new GameObject("White Key Separator", typeof(MeshFilter), typeof(MeshRenderer));
            separator.layer = 2; // Ignore Raycast; excluded from interaction queries.
            separator.transform.SetParent(whiteKey, false);
            separator.transform.localPosition = new Vector3(
                -BaseWhiteKeyPitchMeters * 0.5f / BaseWhiteKeyWidthMeters,
                (BaseWhiteKeyHeightMeters * 0.5f + WhiteKeySeparatorCenterAboveTopMeters) /
                    BaseWhiteKeyHeightMeters,
                0f);
            separator.transform.localScale = new Vector3(
                WhiteKeySeparatorWidthMeters / BaseWhiteKeyWidthMeters,
                WhiteKeySeparatorHeightMeters / BaseWhiteKeyHeightMeters,
                1f);
            separator.GetComponent<MeshFilter>().sharedMesh = whiteKey.GetComponent<MeshFilter>().sharedMesh;
            var renderer = separator.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_WhiteKeySeparators.Add(renderer);
        }

        void UpdateWhiteKeySeparatorVisuals()
        {
            var color = new Color(0.18f, 0.18f, 0.18f, m_CalibrationTransparency ? 0.35f : 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            foreach (var renderer in m_WhiteKeySeparators)
            {
                // Compensate only the strip width to keep it 1.2 mm after calibration.
                var scale = renderer.transform.localScale;
                scale.x = WhiteKeySeparatorWidthMeters /
                    (BaseWhiteKeyWidthMeters * KeyboardGeometry.localScale.x);
                renderer.transform.localScale = scale;
                renderer.sharedMaterial = m_CalibrationTransparency ? m_TransparentMaterial : m_SharedMaterial;
                renderer.SetPropertyBlock(block);
            }
        }

        static int WhiteIndexFromC4(int note)
        {
            var index = 0;
            if (note >= 60)
                for (var n = 60; n < note; ++n) { if (!IsBlack(n)) ++index; }
            else
                for (var n = note; n < 60; ++n) { if (!IsBlack(n)) --index; }
            return index;
        }

        static bool IsBlack(int note)
        {
            var pitch = note % 12;
            return pitch == 1 || pitch == 3 || pitch == 6 || pitch == 8 || pitch == 10;
        }
    }
}
