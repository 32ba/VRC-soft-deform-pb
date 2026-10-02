using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformPBMotionCheckWindow : EditorWindow
    {
        private const string WindowTitle = "Motion Quality Check";
        private const string PlayModeArmedKey = "SoftDeformPB.MotionCheck.PlayModeArmed";
        private const double CaptureInterval = 1.0 / 60.0;

        private sealed class MotionPreset
        {
            public readonly string Label;
            public readonly string AssetName;
            public readonly string Purpose;

            public MotionPreset(string label, string assetName, string purpose)
            {
                Label = label;
                AssetName = assetName;
                Purpose = purpose;
            }
        }

        private static readonly MotionPreset[] Presets =
        {
            new MotionPreset("Dance", "[EMOTE 5] Dance", "繰り返す動きで、揺れの大きさと遅れを見る"),
            new MotionPreset("Backflip", "[EMOTE 6] BackFlip", "大きな回転のあと、どう戻るかを見る"),
            new MotionPreset("Laugh", "[EMOTE 1] Laugh", "上半身の細かな動きで、反発を見る"),
            new MotionPreset("Cheer", "[EMOTE 4] Cheer", "短い上下動で、揺れの出方を見る"),
            new MotionPreset("Fall", "[EMOTE 7] Die", "寝る姿勢への切替と、その後の動きを見る")
        };

        private const string GestureManagerMotionRoot =
            "Packages/vrchat.blackstartx.gesture-manager/Resources/Gm/Animations/Emote";

        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private AnimationClip motion;
        [SerializeField] private Transform leftBreast;
        [SerializeField] private Transform rightBreast;
        [SerializeField] private Transform chestReference;
        [SerializeField] private int presetIndex;
        [SerializeField] private bool loop = true;
        [SerializeField] private float playbackSpeed = 1.0f;
        [SerializeField] private float previewTime;
        [SerializeField] private bool editPreviewPlaying;
        [SerializeField] private bool runtimePlaying = true;
        [SerializeField] private bool captureEnabled = true;
        [SerializeField] private SoftDeformMotionMetricsCollector metrics = new SoftDeformMotionMetricsCollector();
        [SerializeField] private bool hasAuthoredReference;
        [SerializeField] private Vector3 authoredLeftPosition;
        [SerializeField] private Vector3 authoredRightPosition;
        [SerializeField] private Vector3 authoredLeftScale;
        [SerializeField] private Vector3 authoredRightScale;
        [SerializeField] private GameObject previewTemplate;
        [SerializeField] private SoftDeformPreviewSettings comparisonSettings;
        [SerializeField] private SoftDeformPreviewSettings tuningSettings;
        [SerializeField] private bool adoptAfterStop;
        [SerializeField] private bool autoReflect = true;
        [SerializeField] private bool previewBaseline;
        [SerializeField] private bool showTuningDetails;
        [SerializeField] private bool originalRuntimeActive;
        [SerializeField] private int previewRevision;
        [SerializeField] private SoftDeformPreviewOverrides previewOverrides = new SoftDeformPreviewOverrides();

        [NonSerialized] private PlayableGraph runtimeGraph;
        [NonSerialized] private AnimatorControllerPlayable runtimeFx;
        [NonSerialized] private AvatarMask runtimeFxMask;
        [NonSerialized] private Action refreshRuntimeParameters;
        [NonSerialized] private SoftDeformLivePreview livePreview;
        [NonSerialized] private SoftDeformPreviewShapeLayer runtimeShapeLayer;
        private readonly SoftDeformPreviewOverridePanel overridePanel = new SoftDeformPreviewOverridePanel();
        [NonSerialized] private bool rebuildPending;
        [NonSerialized] private double tuningChangedAt;
        [NonSerialized] private bool domainReloading;
        private readonly SoftDeformEasyPanel liveEasy = new SoftDeformEasyPanel();
        [NonSerialized] private AnimationClipPlayable runtimeClip;
        [NonSerialized] private Animator runtimeAnimator;
        [NonSerialized] private GameObject runtimeAvatarRoot;
        [NonSerialized] private Transform runtimeLeftBreast;
        [NonSerialized] private Transform runtimeRightBreast;
        [NonSerialized] private Transform runtimeLeftVisual;
        [NonSerialized] private Transform runtimeRightVisual;
        [NonSerialized] private Transform runtimeChestReference;
        [NonSerialized] private double runtimeAvatarObservedAt;
        [NonSerialized] private double lastEditorUpdate;
        [NonSerialized] private double lastCaptureTime;
        [NonSerialized] private string statusMessage;
        [NonSerialized] private MessageType statusType;
        [NonSerialized] private Vector2 scrollPosition;
        [NonSerialized] private bool showMeasurements;

        [MenuItem("Tools/Soft Deform PB/Motion Quality Check")]
        private static void Open()
        {
            SoftDeformPBMotionCheckWindow window = GetWindow<SoftDeformPBMotionCheckWindow>(WindowTitle);
            window.minSize = new Vector2(480.0f, 640.0f);
            window.Show();
        }

        internal static void OpenFor(SoftDeformPBSetup setup)
        {
            var window = GetWindow<SoftDeformPBMotionCheckWindow>(WindowTitle);
            if (window.avatarRoot == setup.gameObject && (window.runtimeGraph.IsValid() || window.tuningSettings != null))
            {
                window.Show();
                return;
            }
            window.StopEditPreview();
            window.DestroyRuntimeGraph();
            window.ReleaseLivePreview();
            SessionState.SetBool(PlayModeArmedKey, false);
            window.avatarRoot = setup.gameObject;
            window.previewOverrides = new SoftDeformPreviewOverrides();
            window.comparisonSettings = null;
            window.tuningSettings = null;
            window.RefreshAvatarReferences();
            window.metrics.Reset();
            window.previewTime = 0;
            window.SetStatus("このSetupを選択しました。揺れを見るにはPlay Modeの物理確認を開始してください。", MessageType.Info);
            window.Show();
        }

        private void OnEnable()
        {
            if (previewOverrides == null) previewOverrides = new SoftDeformPreviewOverrides();
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(480.0f, 640.0f);
            if (position.width < minSize.x || position.height < minSize.y || position.y < 20.0f)
            {
                position = new Rect(
                    Mathf.Max(40.0f, position.x),
                    Mathf.Max(60.0f, position.y),
                    Mathf.Max(minSize.x, position.width),
                    Mathf.Max(minSize.y, position.height));
            }
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeDomainReload;
            domainReloading = false;
            lastEditorUpdate = EditorApplication.timeSinceStartup;

            if (avatarRoot == null) UseSelection(false);
            if (motion == null || motion.length <= 0.0f) LoadPreset(0, false);
            RefreshAvatarReferences();
            if (EditorApplication.isPlaying && PreviewSetup != null && SessionState.GetBool(PlayModeArmedKey, false))
                QueueRebuild(true);
        }

        private void BeforeDomainReload() { domainReloading = true; }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeDomainReload;
            StopEditPreview();
            DestroyRuntimeGraph();
            liveEasy.Release();
            if (domainReloading && livePreview != null)
            {
                livePreview.Dispose();
                livePreview = null;
                if (EditorApplication.isPlaying && avatarRoot != null) avatarRoot.SetActive(originalRuntimeActive);
            }
            // Keep the authored template across the reload that enters Play Mode.
            if (!domainReloading && (!EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying))
            {
                ReleaseLivePreview();
                SessionState.EraseBool(PlayModeArmedKey);
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.HelpBox(
                "揺れの確認を開始したら、この画面のパッドで調整できます。手を離すと確認用アバターを更新し、同じ動きを最初から再生します。",
                MessageType.Info);

            if (EditorApplication.isPlaying)
                EditorGUILayout.LabelField("確認中のアバター", avatarRoot != null ? avatarRoot.name : "未選択");
            else using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawInputs();
            EditorGUILayout.Space();
            DrawMotionPicker();
            EditorGUILayout.Space();
            DrawPreviewControls();
            bool running = EditorApplication.isPlaying && SessionState.GetBool(PlayModeArmedKey, false) && runtimeAvatarRoot != null;
            if (overridePanel.Draw(running ? runtimeAvatarRoot : avatarRoot, previewOverrides, running))
            {
                refreshRuntimeParameters?.Invoke();
                runtimeShapeLayer?.Refresh(previewOverrides);
                if (runtimeGraph.IsValid() && EditorApplication.isPaused) runtimeGraph.Evaluate(0);
                SceneView.RepaintAll();
            }
            DrawLiveTuning();
            EditorGUILayout.Space();
            showMeasurements = EditorGUILayout.Foldout(showMeasurements, "計測値を確認する", true);
            if (showMeasurements) DrawMeasurements();

            if (!string.IsNullOrEmpty(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.EndScrollView();
        }

        private void DrawInputs()
        {
            EditorGUILayout.LabelField("対象のアバター", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            GameObject nextAvatar = (GameObject)EditorGUILayout.ObjectField("アバターのルート", avatarRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                StopEditPreview();
                ReleaseLivePreview();
                avatarRoot = nextAvatar;
                previewOverrides = new SoftDeformPreviewOverrides();
                comparisonSettings = null;
                tuningSettings = null;
                RefreshAvatarReferences();
                metrics.Reset();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("今の選択を使う")) UseSelection(true);
                if (GUILayout.Button("Hierarchyで示す") && avatarRoot != null) EditorGUIUtility.PingObject(avatarRoot);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("左の胸ボーン", leftBreast, typeof(Transform), true);
                EditorGUILayout.ObjectField("右の胸ボーン", rightBreast, typeof(Transform), true);
                EditorGUILayout.ObjectField("計測の基準", chestReference, typeof(Transform), true);
            }

            string validation = ValidateTarget();
            if (!string.IsNullOrEmpty(validation)) EditorGUILayout.HelpBox(validation, MessageType.Warning);
        }

        private void DrawMotionPicker()
        {
            EditorGUILayout.LabelField("比較に使う動き", EditorStyles.boldLabel);
            string[] labels = Presets.Select(preset => preset.Label).ToArray();
            EditorGUI.BeginChangeCheck();
            int nextPreset = EditorGUILayout.Popup("確認用モーション", presetIndex, labels);
            if (EditorGUI.EndChangeCheck()) LoadPreset(nextPreset, true);

            EditorGUILayout.LabelField(Presets[presetIndex].Purpose, EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            AnimationClip nextMotion = (AnimationClip)EditorGUILayout.ObjectField(
                "アニメーションクリップ",
                motion,
                typeof(AnimationClip),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                StopEditPreview();
                motion = nextMotion;
                previewTime = 0.0f;
                metrics.Reset();
                MotionChanged();
            }

            if (motion != null)
            {
                EditorGUILayout.LabelField(
                    "クリップ情報",
                    $"{motion.length:0.00} s, {motion.frameRate:0.#} fps, {(motion.isHumanMotion ? "Humanoid" : "Generic/transform")}");
            }

            if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox(
                "インストール済みのGesture Managerから確認用の動きを選びます。プロジェクト内の他のAnimationClipも指定できます。",
                MessageType.None);
        }

        private void DrawPreviewControls()
        {
            EditorGUILayout.LabelField("再生して確認", EditorStyles.boldLabel);
            loop = EditorGUILayout.Toggle("繰り返す", loop);
            EditorGUI.BeginChangeCheck();
            playbackSpeed = EditorGUILayout.Slider("再生速度", playbackSpeed, 0.1f, 2.0f);
            if (EditorGUI.EndChangeCheck()) ApplyRuntimeSpeed();

            float clipLength = motion != null ? Mathf.Max(0.0001f, motion.length) : 1.0f;
            EditorGUI.BeginChangeCheck();
            previewTime = EditorGUILayout.Slider("再生位置（秒）", previewTime, 0.0f, clipLength);
            if (EditorGUI.EndChangeCheck())
            {
                if (EditorApplication.isPlaying && runtimeGraph.IsValid())
                {
                    runtimeClip.SetTime(previewTime);
                    runtimeClip.SetDone(false);
                }
                else
                {
                    SampleEditPose();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !CanPreview()))
                {
                    if (GUILayout.Button(editPreviewPlaying ? "姿勢の再生を一時停止" : "姿勢だけを再生"))
                    {
                        editPreviewPlaying = !editPreviewPlaying;
                        if (editPreviewPlaying) StartEditPreview();
                    }
                    if (GUILayout.Button("姿勢の再生を停止")) StopEditPreview();
                }
            }

            bool armed = SessionState.GetBool(PlayModeArmedKey, false);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!EditorApplication.isPlaying)
                {
                    using (new EditorGUI.DisabledScope(!CanRunPhysicsCheck()))
                    {
                        if (GUILayout.Button("揺れを確認する（Play Mode）", GUILayout.Height(26.0f)))
                            StartPlayModeCheck();
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!armed || !runtimeGraph.IsValid()))
                    {
                        if (GUILayout.Button(runtimePlaying ? "動きを一時停止" : "動きを再開", GUILayout.Height(26.0f)))
                        {
                            runtimePlaying = !runtimePlaying;
                            ApplyRuntimeSpeed();
                        }
                    }
                    using (new EditorGUI.DisabledScope(PreviewSetup == null))
                        if (GUILayout.Button("採用して終了", GUILayout.Height(26))) AdoptAndStop();
                    if (GUILayout.Button("終了（調整値を保留）", GUILayout.Height(26.0f)))
                        EditorApplication.isPlaying = false;
                }
            }
        }

        private SoftDeformPBSetup PreviewSetup => previewTemplate != null
            ? previewTemplate.GetComponentInChildren<SoftDeformPBSetup>(true) : null;

        private void DrawLiveTuning()
        {
            if (!EditorApplication.isPlaying)
            {
                if (tuningSettings == null || tuningSettings.SameAs(comparisonSettings)) return;
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("調整した値を保留しています。採用すると元のSetupに反映します。Undoで戻せます。", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("調整値を採用")) AdoptTuning();
                    if (GUILayout.Button("調整値を破棄")) { tuningSettings = null; comparisonSettings = null; }
                }
                return;
            }
            SoftDeformPBSetup setup = PreviewSetup;
            if (setup == null || !SessionState.GetBool(PlayModeArmedKey, false)) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("動きを見ながら調整", EditorStyles.boldLabel);
            autoReflect = EditorGUILayout.Toggle("手を離したら自動で反映", autoReflect);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(previewBaseline))
                    if (GUILayout.Button("A · 変更前を見る")) { previewBaseline = true; QueueRebuild(true); }
                using (new EditorGUI.DisabledScope(!previewBaseline))
                    if (GUILayout.Button("B · 調整中を見る")) { previewBaseline = false; QueueRebuild(true); }
                if (GUILayout.Button("最初から再生")) QueueRebuild(true);
            }
            EditorGUILayout.LabelField(previewBaseline ? "表示中：A 変更前。パッドの値はBの調整値です。" :
                $"表示中：B 調整中  ·  反映 {previewRevision} 回", EditorStyles.wordWrappedMiniLabel);

            using (var settings = new SerializedObject(setup))
            {
                settings.Update();
                VRCPhysBone left = FindAuthoredPhysBone(setup, setup.leftBreast);
                VRCPhysBone right = FindAuthoredPhysBone(setup, setup.rightBreast);
                liveEasy.Draw(settings, left, right);
                showTuningDetails = EditorGUILayout.Foldout(showTuningDetails, "確認中の詳細設定", true);
                if (showTuningDetails)
                {
                    SerializedProperty property = settings.GetIterator();
                    bool children = true;
                    while (property.NextVisible(children))
                    {
                        children = false;
                        if (property.name == "m_Script" || property.name == "leftBreast" || property.name == "rightBreast") continue;
                        var label = SoftDeformTuningGuide.TryGet(property.name, out var entry)
                            ? new GUIContent(entry.Label, entry.Effect) : new GUIContent(property.displayName);
                        EditorGUILayout.PropertyField(property, label, true);
                    }
                }
                settings.ApplyModifiedProperties();
            }
            ObserveTuningChanges();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("今の値を反映")) { previewBaseline = false; QueueRebuild(true); }
                if (GUILayout.Button("この設定を採用して終了", GUILayout.Height(26)))
                    AdoptAndStop();
            }
            EditorGUILayout.LabelField("この画面の調整値を採用すると元のSetupに戻します。更新には生成と物理の初期化のため少し時間がかかります。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static VRCPhysBone FindAuthoredPhysBone(SoftDeformPBSetup setup, Transform root)
        {
            return root == null ? null : setup.GetComponentsInChildren<VRCPhysBone>(true)
                .FirstOrDefault(bone => bone.GetRootTransform() == root);
        }

        private void ObserveTuningChanges()
        {
            SoftDeformPBSetup setup = PreviewSetup;
            if (setup == null) return;
            var next = SoftDeformPreviewSettings.Capture(setup);
            if (next.SameAs(tuningSettings)) return;
            tuningSettings = next;
            tuningChangedAt = EditorApplication.timeSinceStartup;
            if (autoReflect && !previewBaseline) rebuildPending = true;
            SetStatus(previewBaseline ? "Bの調整値を変更しました。『B · 調整中を見る』で確認できます。" :
                autoReflect ? "変更を受け付けました。パッドから手を離すと反映します。" : "調整値を変更しました。『今の値を反映』で確認できます。", MessageType.Info);
        }

        private void QueueRebuild(bool immediate)
        {
            rebuildPending = true;
            tuningChangedAt = immediate ? EditorApplication.timeSinceStartup - 1 : EditorApplication.timeSinceStartup;
        }

        private void MotionChanged()
        {
            if (EditorApplication.isPlaying && PreviewSetup != null) QueueRebuild(true);
            else if (runtimeGraph.IsValid()) DestroyRuntimeGraph();
        }

        private void RebuildLivePreview()
        {
            rebuildPending = false;
            try
            {
                SoftDeformLivePreview next = SoftDeformLivePreview.Build(previewTemplate,
                    previewBaseline ? comparisonSettings : tuningSettings);
                DestroyRuntimeGraph();
                livePreview?.Dispose();
                livePreview = next;
                if (avatarRoot != null) avatarRoot.SetActive(false);
                next.Avatar.SetActive(true);
                runtimeAvatarRoot = next.Avatar;
                runtimeAvatarObservedAt = EditorApplication.timeSinceStartup;
                previewTime = 0;
                ResetMeasurements();
                previewRevision++;
                SetStatus("調整値を反映しました。物理の初期化後、同じ動きを最初から再生します。", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("変更を反映できませんでした。直前の確認用アバターを保持しています。 " + exception.Message, MessageType.Error);
            }
        }

        private void AdoptTuning()
        {
            SoftDeformPBSetup setup = avatarRoot != null ? avatarRoot.GetComponentInChildren<SoftDeformPBSetup>(true) : null;
            if (setup == null)
            {
                adoptAfterStop = false;
                SetStatus("元のSetupが見つかりません。調整値は保留しています。", MessageType.Error);
                return;
            }
            tuningSettings?.Apply(setup, comparisonSettings, true);
            tuningSettings = null;
            comparisonSettings = null;
            adoptAfterStop = false;
            SetStatus("調整値を元のSetupに採用しました。Undoで元に戻せます。", MessageType.Info);
        }

        private void AdoptAndStop()
        {
            if (PreviewSetup == null) return;
            tuningSettings = SoftDeformPreviewSettings.Capture(PreviewSetup);
            adoptAfterStop = true;
            EditorApplication.isPlaying = false;
        }

        private void ReleaseLivePreview()
        {
            livePreview?.Dispose();
            livePreview = null;
            if (EditorApplication.isPlaying && avatarRoot != null) avatarRoot.SetActive(originalRuntimeActive);
            if (previewTemplate != null) UnityEngine.Object.DestroyImmediate(previewTemplate);
            previewTemplate = null;
            rebuildPending = false;
        }

        private void DrawMeasurements()
        {
            EditorGUILayout.LabelField("Mechanical measurements", EditorStyles.boldLabel);
            captureEnabled = EditorGUILayout.Toggle("Capture while playing", captureEnabled);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Samples", metrics.SampleCount.ToString());
                EditorGUILayout.LabelField("Duration", $"{metrics.Duration:0.00} s");
                EditorGUILayout.LabelField("Invalid", metrics.InvalidSampleCount.ToString());
            }

            DrawMetric("Peak displacement", metrics.PeakLeftDisplacement * 100.0f, metrics.PeakRightDisplacement * 100.0f, "cm");
            DrawMetric("RMS displacement", metrics.RmsLeftDisplacement * 100.0f, metrics.RmsRightDisplacement * 100.0f, "cm");
            DrawMetric("Peak speed", metrics.PeakLeftSpeed, metrics.PeakRightSpeed, "m/s");
            EditorGUILayout.LabelField("Peak L/R magnitude difference", $"{metrics.PeakMagnitudeAsymmetry * 100.0f:0.000} cm");
            EditorGUILayout.LabelField("RMS L/R magnitude difference", $"{metrics.RmsMagnitudeAsymmetry * 100.0f:0.000} cm");
            EditorGUILayout.LabelField("Peak scale coefficient deviation", $"{metrics.PeakRelativeScaleDeviation * 100.0f:0.00} %");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset measurements")) ResetMeasurements();
                using (new EditorGUI.DisabledScope(metrics.SampleCount == 0))
                {
                    if (GUILayout.Button("Export CSV")) ExportCsv();
                }
            }

            EditorGUILayout.HelpBox(
                (metrics.HasExplicitBaseline
                    ? "Baseline: authored pose captured before entering Play Mode. Static sag is included. "
                    : "Baseline: first measured sample. Static sag before capture is not included. ") +
                "Positions are measured in the common chest frame. These are bone measurements, not final mesh silhouette or volume measurements.",
                metrics.InvalidSampleCount > 0 ? MessageType.Warning : MessageType.None);
        }

        private static void DrawMetric(string label, float left, float right, string unit)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                GUILayout.Label($"L {left:0.000} {unit}");
                GUILayout.Label($"R {right:0.000} {unit}");
            }
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double deltaTime = Math.Max(0.0, now - lastEditorUpdate);
            lastEditorUpdate = now;

            if (!EditorApplication.isPlaying)
            {
                if (editPreviewPlaying && CanPreview())
                {
                    previewTime = AdvanceTime(previewTime, deltaTime);
                    SampleEditPose();
                    Repaint();
                }
                return;
            }

            if (!SessionState.GetBool(PlayModeArmedKey, false)) return;
            ObserveTuningChanges();
            if (rebuildPending && GUIUtility.hotControl == 0 && !EditorApplication.isPaused &&
                now - tuningChangedAt >= .35) RebuildLivePreview();
            if (!runtimeGraph.IsValid()) TryCreateRuntimeGraph();
            if (!runtimeGraph.IsValid()) return;
            refreshRuntimeParameters?.Invoke();
            runtimeShapeLayer?.Refresh(previewOverrides);

            double currentTime = runtimeClip.GetTime();
            if (loop && motion != null && motion.length > 0.0f && currentTime >= motion.length)
            {
                currentTime %= motion.length;
                runtimeClip.SetTime(currentTime);
                runtimeClip.SetDone(false);
            }
            previewTime = (float)Math.Min(currentTime, motion != null ? motion.length : currentTime);

            if (captureEnabled && now - lastCaptureTime >= CaptureInterval)
            {
                CaptureSample(now);
                lastCaptureTime = now;
            }
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PlayModeArmedKey, false))
            {
                lastCaptureTime = 0.0;
                runtimeAvatarObservedAt = 0.0;
                originalRuntimeActive = avatarRoot != null && avatarRoot.activeSelf;
                if (PreviewSetup != null) QueueRebuild(true);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                DestroyRuntimeGraph();
                if (PreviewSetup != null) tuningSettings = SoftDeformPreviewSettings.Capture(PreviewSetup);
                ReleaseLivePreview();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Scene rollback can restore a template that was destroyed while exiting Play Mode.
                ReleaseLivePreview();
                SessionState.EraseBool(PlayModeArmedKey);
                runtimePlaying = true;
                RefreshAvatarReferences();
                if (adoptAfterStop) AdoptTuning();
                Repaint();
            }
        }

        private void UseSelection(bool reportFailure)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                if (reportFailure) SetStatus("Select an avatar or one of its children first.", MessageType.Warning);
                return;
            }

            SoftDeformPBSetup setup = selected.GetComponentInParent<SoftDeformPBSetup>();
            if (setup == null) setup = selected.GetComponentInChildren<SoftDeformPBSetup>(true);
            if (setup == null)
            {
                if (reportFailure) SetStatus("The selection is not inside an avatar with SoftDeformPB Setup.", MessageType.Warning);
                return;
            }

            StopEditPreview();
            ReleaseLivePreview();
            avatarRoot = setup.gameObject;
            previewOverrides = new SoftDeformPreviewOverrides();
            comparisonSettings = null;
            tuningSettings = null;
            RefreshAvatarReferences();
            metrics.Reset();
            SetStatus("Avatar selected from SoftDeformPB Setup.", MessageType.Info);
        }

        private void RefreshAvatarReferences()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(PlayModeArmedKey, false))
                hasAuthoredReference = false;
            leftBreast = null;
            rightBreast = null;
            chestReference = null;
            if (avatarRoot == null) return;

            SoftDeformPBSetup setup = EditorApplication.isPlaying && PreviewSetup != null ? PreviewSetup :
                avatarRoot.GetComponentInChildren<SoftDeformPBSetup>(true);
            if (setup == null) return;
            leftBreast = setup.leftBreast;
            rightBreast = setup.rightBreast;
            chestReference = FindCommonAncestor(leftBreast, rightBreast, avatarRoot.transform);
        }

        private void LoadPreset(int index, bool report)
        {
            presetIndex = Mathf.Clamp(index, 0, Presets.Length - 1);
            MotionPreset preset = Presets[presetIndex];
            AnimationClip found = FindDistributedClip(preset.AssetName);
            if (found == null)
            {
                motion = null;
                if (report)
                    SetStatus(
                        $"Could not find {preset.AssetName}. Install Gesture Manager or choose any project AnimationClip.",
                        MessageType.Warning);
                return;
            }

            StopEditPreview();
            motion = found;
            previewTime = 0.0f;
            metrics.Reset();
            if (report) SetStatus($"Selected distributed motion: {preset.Label}.", MessageType.Info);
            MotionChanged();
        }

        private static AnimationClip FindDistributedClip(string assetName)
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { GestureManagerMotionRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip exact = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(clip => clip.name == assetName && clip.length > 0.0f);
                if (exact != null) return exact;
            }
            return null;
        }

        private void StartEditPreview()
        {
            if (!CanPreview()) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            editPreviewPlaying = true;
            lastEditorUpdate = EditorApplication.timeSinceStartup;
            SampleEditPose();
        }

        private void StopEditPreview()
        {
            editPreviewPlaying = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            SceneView.RepaintAll();
        }

        private void SampleEditPose()
        {
            if (!CanPreview() || EditorApplication.isPlaying) return;
            try
            {
                if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(avatarRoot, motion, Mathf.Clamp(previewTime, 0.0f, motion.length));
                AnimationMode.EndSampling();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                editPreviewPlaying = false;
                SetStatus("Pose preview failed: " + exception.Message, MessageType.Error);
            }
        }

        private void StartPlayModeCheck()
        {
            string validation = ValidatePhysicsCheck();
            if (!string.IsNullOrEmpty(validation))
            {
                SetStatus(validation, MessageType.Error);
                return;
            }

            StopEditPreview();
            if (!EditorApplication.isPlaying)
            {
                RefreshAvatarReferences();
                if (leftBreast == null || rightBreast == null || chestReference == null) return;
                authoredLeftPosition = chestReference.InverseTransformPoint(leftBreast.position);
                authoredRightPosition = chestReference.InverseTransformPoint(rightBreast.position);
                authoredLeftScale = leftBreast.localScale;
                authoredRightScale = rightBreast.localScale;
                hasAuthoredReference = true;
                originalRuntimeActive = avatarRoot.activeSelf;
                SoftDeformPBSetup setup = avatarRoot.GetComponentInChildren<SoftDeformPBSetup>(true);
                if (comparisonSettings == null || tuningSettings == null)
                {
                    comparisonSettings = SoftDeformPreviewSettings.Capture(setup);
                    tuningSettings = SoftDeformPreviewSettings.Capture(setup);
                }
                if (previewTemplate != null) UnityEngine.Object.DestroyImmediate(previewTemplate);
                previewTemplate = SoftDeformLivePreview.CreateTemplate(avatarRoot);
                tuningSettings.Apply(PreviewSetup);
                previewBaseline = false;
                previewRevision = 0;
                adoptAfterStop = false;
            }
            ResetMeasurements();
            previewTime = 0.0f;
            runtimePlaying = true;
            SessionState.SetBool(PlayModeArmedKey, true);
            if (EditorApplication.isPlaying) TryCreateRuntimeGraph();
            else EditorApplication.isPlaying = true;
        }

        private void TryCreateRuntimeGraph()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(PlayModeArmedKey, false) || runtimeGraph.IsValid()) return;

            string validation = ValidatePhysicsCheck();
            if (!string.IsNullOrEmpty(validation))
            {
                SetStatus(validation, MessageType.Error);
                return;
            }

            GameObject candidate = livePreview != null ? livePreview.Avatar : FindProcessedRuntimeAvatar();
            if (PreviewSetup != null && livePreview == null) return;
            if (candidate == null)
            {
                SetStatus("Waiting for NDMF Apply on Play to create the processed avatar...", MessageType.Info);
                return;
            }

            if (runtimeAvatarRoot != candidate)
            {
                runtimeAvatarRoot = candidate;
                runtimeAvatarObservedAt = EditorApplication.timeSinceStartup;
                SetStatus("Processed avatar found. Waiting for PhysBone initialization...", MessageType.Info);
                return;
            }

            if (EditorApplication.timeSinceStartup - runtimeAvatarObservedAt < 1.0) return;

            runtimeLeftBreast = FindGeneratedBreast(candidate.transform, leftBreast.name, "Left");
            runtimeRightBreast = FindGeneratedBreast(candidate.transform, rightBreast.name, "Right");
            runtimeLeftVisual = FindVisualRoot(runtimeLeftBreast, "Left");
            runtimeRightVisual = FindVisualRoot(runtimeRightBreast, "Right");
            runtimeChestReference = FindCommonAncestor(runtimeLeftBreast, runtimeRightBreast, candidate.transform);
            runtimeAnimator = candidate.GetComponent<Animator>();
            if (runtimeAnimator == null || runtimeLeftBreast == null || runtimeRightBreast == null || runtimeChestReference == null)
            {
                SetStatus("The processed avatar is missing its Animator or generated breast roots.", MessageType.Error);
                return;
            }

            runtimeGraph = PlayableGraph.Create("SoftDeformPB Motion Quality Check");
            runtimeGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            runtimeClip = AnimationClipPlayable.Create(runtimeGraph, motion);
            runtimeClip.SetApplyFootIK(true);
            runtimeClip.SetApplyPlayableIK(true);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(runtimeGraph, "Avatar Motion", runtimeAnimator);
            RuntimeAnimatorController fx = SoftDeformLivePreview.FindFxController(candidate);
            if (fx != null)
            {
                runtimeFx = AnimatorControllerPlayable.Create(runtimeGraph, fx);
                var mixer = AnimationLayerMixerPlayable.Create(runtimeGraph, 2);
                runtimeGraph.Connect(runtimeClip, 0, mixer, 0);
                runtimeGraph.Connect(runtimeFx, 0, mixer, 1);
                mixer.SetInputWeight(0, 1);
                mixer.SetInputWeight(1, 1);
                runtimeFxMask = new AvatarMask();
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                    runtimeFxMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
                runtimeFxMask.AddTransformPath(candidate.transform, true);
                mixer.SetLayerMaskFromAvatarMask(1, runtimeFxMask);
                refreshRuntimeParameters = SoftDeformLivePreview.ConnectParameters(candidate, runtimeFx, previewOverrides);
                output.SetSourcePlayable(mixer);
            }
            else output.SetSourcePlayable(runtimeClip);
            runtimeShapeLayer = new SoftDeformPreviewShapeLayer(runtimeGraph, runtimeAnimator, output);
            runtimeShapeLayer.Refresh(previewOverrides);
            runtimeGraph.Play();
            ApplyRuntimeSpeed();
            lastCaptureTime = 0.0;
            SetStatus(
                $"{(previewBaseline ? "A · 変更前" : "B · 調整中")}の揺れを確認中です。パッドから手を離すと更新します。",
                MessageType.Info);
        }

        private void DestroyRuntimeGraph()
        {
            runtimeShapeLayer?.Dispose();
            runtimeShapeLayer = null;
            if (runtimeGraph.IsValid()) runtimeGraph.Destroy();
            refreshRuntimeParameters = null;
            if (runtimeFxMask != null) UnityEngine.Object.DestroyImmediate(runtimeFxMask);
            runtimeFxMask = null;
            runtimeAnimator = null;
            runtimeAvatarRoot = null;
            runtimeLeftBreast = null;
            runtimeRightBreast = null;
            runtimeLeftVisual = null;
            runtimeRightVisual = null;
            runtimeChestReference = null;
            runtimeAvatarObservedAt = 0.0;
        }

        private void ApplyRuntimeSpeed()
        {
            if (runtimeGraph.IsValid()) runtimeClip.SetSpeed(runtimePlaying ? playbackSpeed : 0.0);
        }

        private void CaptureSample(double clipTime)
        {
            if (runtimeLeftBreast == null || runtimeRightBreast == null || runtimeChestReference == null) return;
            metrics.AddSample(
                clipTime,
                runtimeChestReference.InverseTransformPoint(runtimeLeftBreast.position),
                runtimeChestReference.InverseTransformPoint(runtimeRightBreast.position),
                Vector3.Scale(runtimeLeftBreast.localScale, runtimeLeftVisual != null ? runtimeLeftVisual.localScale : Vector3.one),
                Vector3.Scale(runtimeRightBreast.localScale, runtimeRightVisual != null ? runtimeRightVisual.localScale : Vector3.one));
        }

        private void ResetMeasurements()
        {
            if (hasAuthoredReference)
                metrics.SetReferencePose(authoredLeftPosition, authoredRightPosition, authoredLeftScale, authoredRightScale);
            else metrics.Reset();
        }

        private GameObject FindProcessedRuntimeAvatar()
        {
            if (avatarRoot == null) return null;
            return UnityEngine.Object.FindObjectsOfType<Animator>(true)
                .Where(animator => animator.gameObject.activeInHierarchy)
                .Where(animator => animator.gameObject.name.StartsWith(avatarRoot.name, StringComparison.Ordinal))
                .Where(animator => HasGeneratedMotionRoot(animator.transform, "Left"))
                .Where(animator => HasGeneratedMotionRoot(animator.transform, "Right"))
                .Select(animator => animator.gameObject)
                .FirstOrDefault();
        }

        private static bool HasGeneratedMotionRoot(Transform root, string sideName)
        {
            string expectedName = $"SoftDeformPB Motion Root {sideName}";
            return root.GetComponentsInChildren<Transform>(true).Any(transform => transform.name == expectedName);
        }

        private static Transform FindVisualRoot(Transform physicalRoot, string sideName)
        {
            if (physicalRoot == null) return null;
            string prefix = "SoftDeformPB Visual " + sideName;
            return physicalRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(child =>
                child.name.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static Transform FindGeneratedBreast(Transform root, string originalName, string sideName)
        {
            string expectedParent = $"SoftDeformPB Motion Restore {sideName}";
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform =>
                    transform.name == originalName &&
                    transform.parent != null &&
                    transform.parent.name == expectedParent);
        }

        private float AdvanceTime(float current, double deltaTime)
        {
            if (motion == null || motion.length <= 0.0f) return 0.0f;
            float next = current + (float)deltaTime * playbackSpeed;
            if (loop) return next % motion.length;
            if (next >= motion.length) editPreviewPlaying = false;
            return Mathf.Min(next, motion.length);
        }

        private bool CanPreview()
        {
            return avatarRoot != null && motion != null && avatarRoot.GetComponent<Animator>() != null;
        }

        private bool CanRunPhysicsCheck()
        {
            return string.IsNullOrEmpty(ValidatePhysicsCheck());
        }

        private string ValidateTarget()
        {
            if (avatarRoot == null) return "Choose an avatar root with SoftDeformPB Setup.";
            if (avatarRoot.GetComponent<Animator>() == null) return "The avatar root needs an Animator.";
            if (leftBreast == null || rightBreast == null) return "SoftDeformPB Setup must reference both breast bones.";
            if (chestReference == null) return "A common chest measurement frame could not be resolved.";
            return string.Empty;
        }

        private string ValidatePhysicsCheck()
        {
            string targetValidation = ValidateTarget();
            if (!string.IsNullOrEmpty(targetValidation)) return targetValidation;
            if (motion == null) return "Choose an AnimationClip.";
            if (motion.length <= 0.0f) return "The selected AnimationClip has no duration.";
            return string.Empty;
        }

        private void ExportCsv()
        {
            string defaultName = motion != null ? $"SoftDeformPB_{motion.name}_measurements.csv" : "SoftDeformPB_measurements.csv";
            string path = EditorUtility.SaveFilePanel("Export SoftDeformPB measurements", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;
            metrics.WriteCsv(path);
            SetStatus($"Exported {metrics.SampleCount} samples to {path}", MessageType.Info);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private static Transform FindCommonAncestor(Transform left, Transform right, Transform boundary)
        {
            if (left == null || right == null) return null;
            for (Transform candidate = left.parent; candidate != null; candidate = candidate.parent)
            {
                if (right.IsChildOf(candidate)) return candidate;
                if (candidate == boundary) break;
            }
            return null;
        }
    }
}
