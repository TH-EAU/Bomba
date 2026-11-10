using System;
using System.Collections.Generic;
using Godot;
using Bomba.Core;
using Bomba.Vehicles;
using Godot.Collections;

namespace Bomba.Effects;

/// <summary>
/// Flou de mouvement de caméra. Le nœud accroche un <see cref="MotionBlurEffect"/> au compositeur de la caméra courante,
/// après les nuages (qui sont composés avant les transparents) : virages et vitesse étirent le décor, les nuages et la
/// pluie ; l'engin et ce qui est proche restent nets.
/// </summary>
public partial class MotionBlur : Node, IVehicleTracker
{
    public Vehicle Target { get; set; }

    /// <summary>Force du flou (part du déplacement d'une image ; ≈ obturation). 0 = désactivé.</summary>
    [Export(PropertyHint.Range, "0,2,0.05")] public float Strength { get; set; } = 0.6f;
    /// <summary>Longueur maximale du flou (fraction de l'écran).</summary>
    [Export(PropertyHint.Range, "0,0.1,0.005")] public float MaxLength { get; set; } = 0.035f;
    /// <summary>Distance (m) en deçà de laquelle tout reste net (l'engin bouge avec la caméra).</summary>
    [Export] public float NearClear { get; set; } = 12f;
    [Export(PropertyHint.Range, "4,24,1")] public int Samples { get; set; } = 10;

    [ExportGroup("Nitro")]
    /// <summary>Force du flou pendant la nitro.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float NitroStrength { get; set; } = 1.6f;
    /// <summary>Longueur maximale du flou pendant la nitro.</summary>
    [Export(PropertyHint.Range, "0,0.15,0.005")] public float NitroMaxLength { get; set; } = 0.07f;
    /// <summary>Temps de montée et de descente du flou de nitro (s).</summary>
    [Export] public float NitroBlendTime { get; set; } = 0.3f;

    private readonly MotionBlurEffect _effect = new();
    private Camera3D _camera;
    private float _nitro;

    public override void _Ready() => AddToGroup(IVehicleTracker.GroupName);

    public override void _Process(double delta)
    {
        bool nitro = IsInstanceValid(Target) && Target.NitroActive;
        _nitro = Mathf.MoveToward(_nitro, nitro ? 1f : 0f, (float)delta / Mathf.Max(NitroBlendTime, 0.01f));
        float k = Mathf.SmoothStep(0f, 1f, _nitro);
        _effect.Strength = Mathf.Lerp(Strength, NitroStrength, k);
        _effect.MaxLength = Mathf.Lerp(MaxLength, NitroMaxLength, k);
        _effect.NearClear = NearClear;
        _effect.Samples = Mathf.RoundToInt(Samples * (1f + k)); // flou plus long : plus d'échantillons, sinon il se strie
        _effect.Enabled = _effect.Strength > 0f;

        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null || cam == _camera)
            return;
        Compositor comp = cam.Compositor ?? new Compositor();
        var effects = comp.CompositorEffects;
        if (!effects.Contains(_effect))
        {
            effects.Add(_effect);
            comp.CompositorEffects = effects;
        }
        cam.Compositor = comp;
        _camera = cam;
    }
}

/// <summary>Effet de compositeur : copie de l'image, puis flou le long du déplacement reprojeté (compute).</summary>
[GlobalClass]
public partial class MotionBlurEffect : CompositorEffect
{
    public float Strength = 0.6f, MaxLength = 0.035f, NearClear = 12f;
    public int Samples = 10;

    private const int ParamsBytes = 160;
    private static readonly StringName Context = "motion_blur";

    private RenderingDevice _rd;
    private Rid _copyShader, _copyPipeline, _blurShader, _blurPipeline, _sampler, _nearest, _ubo;
    private Projection _prevViewProj;
    private bool _hasPrev, _failed;

    public MotionBlurEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        RenderingServer.CallOnRenderThread(Callable.From(InitRender));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _rd != null)
            foreach (Rid r in new[] { _copyShader, _blurShader, _sampler, _nearest, _ubo })
                if (r.IsValid) _rd.FreeRid(r);
    }

    private void InitRender()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
            return; // rendu factice (headless)
        _copyShader = LoadShader("res://Shaders/Effects/motion_blur_copy.glsl");
        _blurShader = LoadShader("res://Shaders/Effects/motion_blur.glsl");
        if (!_copyShader.IsValid || !_blurShader.IsValid)
        {
            _failed = true;
            return;
        }
        _copyPipeline = _rd.ComputePipelineCreate(_copyShader);
        _blurPipeline = _rd.ComputePipelineCreate(_blurShader);
        _sampler = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        _nearest = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        });
        _ubo = _rd.UniformBufferCreate(ParamsBytes, new byte[ParamsBytes]);
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_rd == null || _failed || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent)
            return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers ||
            renderData.GetRenderSceneData() is not RenderSceneDataRD sceneData)
            return;
        Vector2I size = buffers.GetInternalSize();
        if (size.X == 0 || size.Y == 0)
            return;

        Projection proj = sceneData.GetCamProjection();
        Projection viewProj = proj * new Projection(sceneData.GetCamTransform().AffineInverse());
        if (!_hasPrev)
        {
            _prevViewProj = viewProj;
            _hasPrev = true;
        }
        Projection reproj = _prevViewProj * viewProj.Inverse();
        _prevViewProj = viewProj;

        if (buffers.HasTexture(Context, "src"))
        {
            RDTextureFormat existing = buffers.GetTextureFormat(Context, "src");
            if (existing.Width != size.X || existing.Height != size.Y)
                buffers.ClearContext(Context);
        }
        if (!buffers.HasTexture(Context, "src"))
        {
            uint usage = (uint)(RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
            buffers.CreateTexture(Context, "src", RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage,
                RenderingDevice.TextureSamples.Samples1, size, 1, 1, true, false);
        }
        Rid src = buffers.GetTexture(Context, "src");
        Rid color = buffers.GetColorLayer(0);
        Rid depth = buffers.GetDepthLayer(0);

        var f = new List<float>(ParamsBytes / 4);
        AddProjection(f, reproj);
        AddProjection(f, proj.Inverse());
        f.AddRange(new[] { Strength, MaxLength, NearClear, NearClear * 2f });
        f.AddRange(new[] { (float)size.X, size.Y, Math.Max(Samples, 2), 0f });
        var bytes = new byte[ParamsBytes];
        Buffer.BlockCopy(f.ToArray(), 0, bytes, 0, ParamsBytes);
        _rd.BufferUpdate(_ubo, 0, ParamsBytes, bytes);

        uint gx = (uint)((size.X + 7) / 8), gy = (uint)((size.Y + 7) / 8);
        Rid copySet = UniformSetCacheRD.GetCache(_copyShader, 0, new Array<RDUniform>
        {
            Uniform(RenderingDevice.UniformType.Image, 0, color),
            Uniform(RenderingDevice.UniformType.Image, 1, src),
        });
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _copyPipeline);
        _rd.ComputeListBindUniformSet(list, copySet, 0);
        _rd.ComputeListDispatch(list, gx, gy, 1);
        _rd.ComputeListEnd();

        Rid blurSet = UniformSetCacheRD.GetCache(_blurShader, 0, new Array<RDUniform>
        {
            Uniform(RenderingDevice.UniformType.Image, 0, color),
            Uniform(RenderingDevice.UniformType.SamplerWithTexture, 1, _sampler, src),
            Uniform(RenderingDevice.UniformType.SamplerWithTexture, 2, _nearest, depth),
            Uniform(RenderingDevice.UniformType.UniformBuffer, 3, _ubo),
        });
        list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _blurPipeline);
        _rd.ComputeListBindUniformSet(list, blurSet, 0);
        _rd.ComputeListDispatch(list, gx, gy, 1);
        _rd.ComputeListEnd();
    }

    private Rid LoadShader(string path)
    {
        RDShaderSpirV spirv = GD.Load<RDShaderFile>(path)?.GetSpirV();
        if (spirv == null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            GD.PushError($"[MotionBlur] Échec de compilation {path} : {spirv?.CompileErrorCompute}");
            return default;
        }
        return _rd.ShaderCreateFromSpirV(spirv);
    }

    private static void AddProjection(List<float> f, Projection p)
    {
        f.AddRange(new[] { p.X.X, p.X.Y, p.X.Z, p.X.W, p.Y.X, p.Y.Y, p.Y.Z, p.Y.W });
        f.AddRange(new[] { p.Z.X, p.Z.Y, p.Z.Z, p.Z.W, p.W.X, p.W.Y, p.W.Z, p.W.W });
    }

    private static RDUniform Uniform(RenderingDevice.UniformType type, int binding, params Rid[] ids)
    {
        var u = new RDUniform { UniformType = type, Binding = binding };
        foreach (Rid id in ids)
            u.AddId(id);
        return u;
    }
}
