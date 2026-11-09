using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;

namespace Bomba.Clouds;

/// <summary>
/// Rendu des nuages volumétriques (CompositorEffect, avant les objets transparents).
/// Passe 1 : ray marching en demi-résolution guidé par le SDF du <see cref="CloudField"/>,
///           avec érosion Perlin-Worley à deux passes de détail (forme 64³ + détail 32³).
/// Passe 2 : accumulation temporelle (reprojection + clamp de voisinage) en demi-résolution.
/// Passe 3 : composition pleine résolution avec upsample guidé par la profondeur.
/// Les paramètres publics sont écrits par <see cref="CloudField"/> depuis le thread principal.
/// </summary>
public partial class CloudRenderer : CompositorEffect
{
	private const string Context = "bomba_clouds";
	private const int ShapeNoiseSize = 64;
	private const int DetailNoiseSize = 32;
	private const int ParamsBytes = 464;

	// --- Paramètres (écrits par CloudField) ---
	public byte[] CloudData;
	public Vector3I CloudSize;
	public float TileSize = 2048f, MinAltitude = -10f, MaxAltitude = 230f;
	public Vector2 Offset;
	public Vector3 SunDirection = new(0f, 0.3f, 1f);
	public float SunIntensity = 1f;
	public Color SunColor = Colors.White;
	public float Extinction = 0.05f, NearFade = 26f;
	public Color AmbientTop = new(0.5f, 0.55f, 0.85f), AmbientBottom = new(0.12f, 0.14f, 0.3f);
	/// <summary>Période (m) de la texture de forme ; le détail fin est à 0,22 × cette valeur.</summary>
	public float DetailScale = 220f;
	public float DetailStrength = 0.55f, DetailSpeed = 0.004f;
	public Vector4 Lightning;
	public Color FogColor = new(0.7f, 0.6f, 0.8f);
	public float FogDensity = 0.0006f;
	public int MaxSteps = 96;
	public float StepInside = 6f, MaxDistance = 4000f;
	public int DebugMode;
	public float Time;
	/// <summary>Poids de l'image courante dans l'accumulation temporelle (0 = désactivé → 1).</summary>
	public float TemporalBlend = 0.12f;
	public bool TemporalEnabled = true;
	/// <summary>Exposant du maillage vertical non uniforme (voir CloudField.VerticalExponent).</summary>
	public float VerticalExponent = 1f;
	/// <summary>Distance (m) à laquelle le pas de marche intérieur a doublé.</summary>
	public float StepGrowthDistance = 700f;

	// --- Grille géante (cumulonimbus), superposée à la locale ---
	public byte[] GiantData;
	public Vector3I GiantSize;
	public float GiantTileSize = 49152f, GiantMaxAltitude = 14000f, GiantVerticalExponent = 2.2f;
	/// <summary>Coin du domaine géant dans le monde (hors dérive) : la grille géante ne se répète pas.</summary>
	public Vector2 GiantOrigin;
	/// <summary>Ombre de la Terre : x altitude à l'origine, y tan(abaissement), zw azimut du soleil.</summary>
	public Vector4 Shadow = new(1e6f, 0f, 0f, -1f);
	/// <summary>Rideau de pluie sous l'orage : xy centre (X, Z monde), z rayon (m), w intensité (0 : aucun).</summary>
	public Vector4 Rain;
	/// <summary>x rayon de l'œil du cumulonimbus (m), y montée relative du bruit (convection).</summary>
	public Vector4 StormEye;
	/// <summary>LOD du bruit : distances (m) au-delà desquelles le détail fin / les bourgeons ne sont plus calculés.</summary>
	public float LodFineDistance = 6000f, LodShapeDistance = 20000f;

	private RenderingDevice _rd;
	private Rid _marchShader, _marchPipeline, _temporalShader, _temporalPipeline, _compShader, _compPipeline;
	private Rid _cloudTex, _giantTex, _shapeTex, _detailTex, _cloudSampler, _noiseSampler, _nearestSampler, _linearClampSampler, _ubo;
	private uint _frame;
	private bool _failed;
	private int _historyParity;
	private bool _historyValid;
	private Projection _prevViewProj;

	private readonly byte[] _shapeData;
	private readonly byte[] _detailData;

	public CloudRenderer()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PreTransparent;
		_shapeData = BuildShapeNoise(ShapeNoiseSize);
		_detailData = BuildDetailNoise(DetailNoiseSize);
		RenderingServer.CallOnRenderThread(Callable.From(InitRender));
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete && _rd != null)
		{
			foreach (Rid r in new[] { _cloudTex, _giantTex, _shapeTex, _detailTex, _cloudSampler, _noiseSampler, _nearestSampler,
						 _linearClampSampler, _ubo, _marchShader, _temporalShader, _compShader })
				if (r.IsValid) _rd.FreeRid(r);
		}
	}

	private void InitRender()
	{
		_rd = RenderingServer.GetRenderingDevice();
		if (_rd == null)
			return; // rendu factice (headless) : pas de nuages

		_marchShader = LoadShader("res://Shaders/Clouds/clouds_march.glsl");
		_temporalShader = LoadShader("res://Shaders/Clouds/clouds_temporal.glsl");
		_compShader = LoadShader("res://Shaders/Clouds/clouds_composite.glsl");
		if (!_marchShader.IsValid || !_temporalShader.IsValid || !_compShader.IsValid)
		{
			_failed = true;
			return;
		}
		_marchPipeline = _rd.ComputePipelineCreate(_marchShader);
		_temporalPipeline = _rd.ComputePipelineCreate(_temporalShader);
		_compPipeline = _rd.ComputePipelineCreate(_compShader);

		_cloudSampler = _rd.SamplerCreate(new RDSamplerState
		{
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
			RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			RepeatW = RenderingDevice.SamplerRepeatMode.Repeat,
		});
		_noiseSampler = _rd.SamplerCreate(new RDSamplerState
		{
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
			RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
			RepeatW = RenderingDevice.SamplerRepeatMode.Repeat,
		});
		_nearestSampler = _rd.SamplerCreate(new RDSamplerState
		{
			MinFilter = RenderingDevice.SamplerFilter.Nearest,
			MagFilter = RenderingDevice.SamplerFilter.Nearest,
		});
		_linearClampSampler = _rd.SamplerCreate(new RDSamplerState
		{
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
		});

		_shapeTex = Create3D(_shapeData, new Vector3I(ShapeNoiseSize, ShapeNoiseSize, ShapeNoiseSize), RenderingDevice.DataFormat.R8G8B8A8Unorm);
		_detailTex = Create3D(_detailData, new Vector3I(DetailNoiseSize, DetailNoiseSize, DetailNoiseSize), RenderingDevice.DataFormat.R8G8B8A8Unorm);
		_ubo = _rd.UniformBufferCreate(ParamsBytes, new byte[ParamsBytes]);
	}

	private Rid LoadShader(string path)
	{
		var file = GD.Load<RDShaderFile>(path);
		RDShaderSpirV spirv = file?.GetSpirV();
		if (spirv == null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
		{
			GD.PushError($"[CloudRenderer] Échec de compilation {path} : {spirv?.CompileErrorCompute}");
			return default;
		}
		return _rd.ShaderCreateFromSpirV(spirv);
	}

	private Rid Create3D(byte[] data, Vector3I size, RenderingDevice.DataFormat format)
	{
		var fmt = new RDTextureFormat
		{
			Format = format,
			TextureType = RenderingDevice.TextureType.Type3D,
			Width = (uint)size.X,
			Height = (uint)size.Y,
			Depth = (uint)size.Z,
			ArrayLayers = 1,
			Mipmaps = 1,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
		};
		return _rd.TextureCreate(fmt, new RDTextureView(), new Array<byte[]> { data });
	}

	public override void _RenderCallback(int effectCallbackType, RenderData renderData)
	{
		if (_rd == null || _failed || effectCallbackType != (int)EffectCallbackTypeEnum.PreTransparent)
			return;
		if (!_cloudTex.IsValid)
		{
			if (CloudData == null)
				return;
			_cloudTex = Create3D(CloudData, CloudSize, RenderingDevice.DataFormat.R16G16B16A16Sfloat);
		}
		if (!_giantTex.IsValid)
		{
			if (GiantData == null)
				return;
			_giantTex = Create3D(GiantData, GiantSize, RenderingDevice.DataFormat.R16G16B16A16Sfloat);
		}

		if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers ||
			renderData.GetRenderSceneData() is not RenderSceneDataRD sceneData)
			return;

		Vector2I size = buffers.GetInternalSize();
		if (size.X == 0 || size.Y == 0)
			return;
		var half = new Vector2I((size.X + 1) / 2, (size.Y + 1) / 2);

		if (buffers.HasTexture(Context, "cloud"))
		{
			RDTextureFormat existing = buffers.GetTextureFormat(Context, "cloud");
			if (existing.Width != half.X || existing.Height != half.Y)
				buffers.ClearContext(Context);
		}
		if (!buffers.HasTexture(Context, "cloud"))
		{
			uint usage = (uint)(RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
			buffers.CreateTexture(Context, "cloud", RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage,
				RenderingDevice.TextureSamples.Samples1, half, 1, 1, true, false);
			buffers.CreateTexture(Context, "depth", RenderingDevice.DataFormat.R32G32Sfloat, usage,
				RenderingDevice.TextureSamples.Samples1, half, 1, 1, true, false);
			buffers.CreateTexture(Context, "history0", RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage,
				RenderingDevice.TextureSamples.Samples1, half, 1, 1, true, false);
			buffers.CreateTexture(Context, "history1", RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage,
				RenderingDevice.TextureSamples.Samples1, half, 1, 1, true, false);
			_historyValid = false;
		}
		Rid halfCloud = buffers.GetTexture(Context, "cloud");
		Rid halfDepth = buffers.GetTexture(Context, "depth");
		Rid historyRead = buffers.GetTexture(Context, _historyParity == 0 ? "history0" : "history1");
		Rid historyWrite = buffers.GetTexture(Context, _historyParity == 0 ? "history1" : "history0");

		bool temporal = TemporalEnabled && DebugMode == 0;
		Projection viewProj = UpdateParams(sceneData, half, size, temporal && _historyValid);

		Rid color = buffers.GetColorLayer(0);
		Rid depth = buffers.GetDepthLayer(0);

		Rid marchSet = UniformSetCacheRD.GetCache(_marchShader, 0, new Array<RDUniform>
		{
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 0, _cloudSampler, _cloudTex),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 1, _noiseSampler, _shapeTex),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 2, _nearestSampler, depth),
			Uniform(RenderingDevice.UniformType.Image, 3, halfCloud),
			Uniform(RenderingDevice.UniformType.Image, 4, halfDepth),
			Uniform(RenderingDevice.UniformType.UniformBuffer, 5, _ubo),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 6, _noiseSampler, _detailTex),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 7, _cloudSampler, _giantTex),
		});

		long list = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(list, _marchPipeline);
		_rd.ComputeListBindUniformSet(list, marchSet, 0);
		_rd.ComputeListDispatch(list, (uint)((half.X + 7) / 8), (uint)((half.Y + 7) / 8), 1);
		_rd.ComputeListEnd();

		Rid resolved = halfCloud;
		if (temporal)
		{
			Rid temporalSet = UniformSetCacheRD.GetCache(_temporalShader, 0, new Array<RDUniform>
			{
				Uniform(RenderingDevice.UniformType.SamplerWithTexture, 0, _nearestSampler, halfCloud),
				Uniform(RenderingDevice.UniformType.SamplerWithTexture, 1, _nearestSampler, halfDepth),
				Uniform(RenderingDevice.UniformType.SamplerWithTexture, 2, _linearClampSampler, historyRead),
				Uniform(RenderingDevice.UniformType.Image, 3, historyWrite),
				Uniform(RenderingDevice.UniformType.UniformBuffer, 4, _ubo),
			});
			list = _rd.ComputeListBegin();
			_rd.ComputeListBindComputePipeline(list, _temporalPipeline);
			_rd.ComputeListBindUniformSet(list, temporalSet, 0);
			_rd.ComputeListDispatch(list, (uint)((half.X + 7) / 8), (uint)((half.Y + 7) / 8), 1);
			_rd.ComputeListEnd();
			resolved = historyWrite;
			_historyParity = 1 - _historyParity;
			_historyValid = true;
		}
		else
		{
			_historyValid = false;
		}
		_prevViewProj = viewProj;

		Rid compSet = UniformSetCacheRD.GetCache(_compShader, 0, new Array<RDUniform>
		{
			Uniform(RenderingDevice.UniformType.Image, 0, color),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 1, _nearestSampler, resolved),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 2, _nearestSampler, halfDepth),
			Uniform(RenderingDevice.UniformType.SamplerWithTexture, 3, _nearestSampler, depth),
			Uniform(RenderingDevice.UniformType.UniformBuffer, 4, _ubo),
		});
		list = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(list, _compPipeline);
		_rd.ComputeListBindUniformSet(list, compSet, 0);
		_rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
		_rd.ComputeListEnd();

		if (_frame == 0)
			GD.Print($"[CloudRenderer] premier rendu, {size.X}×{size.Y} (demi-résolution {half.X}×{half.Y}), debug {DebugMode}, temporel {temporal}");
		_frame++;
	}

	/// <summary>Écrit le tampon de paramètres ; renvoie la vue-projection courante (pour la frame suivante).</summary>
	private Projection UpdateParams(RenderSceneDataRD sceneData, Vector2I half, Vector2I full, bool historyValid)
	{
		Projection proj = sceneData.GetCamProjection();
		Transform3D cam = sceneData.GetCamTransform();
		Projection viewProj = proj * new Projection(cam.AffineInverse());
		Vector3 sun = SunDirection.Normalized();

		var f = new List<float>(ParamsBytes / 4);
		AddProjection(f, proj.Inverse());
		AddTransform(f, cam);
		Add(f, cam.Origin.X, cam.Origin.Y, cam.Origin.Z, Time);
		Add(f, sun.X, sun.Y, sun.Z, SunIntensity);
		Add(f, SunColor.R, SunColor.G, SunColor.B, Extinction);
		Add(f, AmbientTop.R, AmbientTop.G, AmbientTop.B, NearFade);
		Add(f, AmbientBottom.R, AmbientBottom.G, AmbientBottom.B, DetailStrength);
		Add(f, TileSize, MinAltitude, MaxAltitude, DetailScale);
		Add(f, Offset.X, Offset.Y, _frame % 1024, DetailSpeed);
		Add(f, Lightning.X, Lightning.Y, Lightning.Z, Lightning.W);
		Add(f, half.X, half.Y, full.X, full.Y);
		Add(f, FogColor.R, FogColor.G, FogColor.B, FogDensity);
		Add(f, MaxSteps, StepInside, MaxDistance, DebugMode);
		AddProjection(f, _prevViewProj);
		Add(f, TemporalBlend, historyValid ? 1f : 0f, VerticalExponent, StepGrowthDistance);
		Add(f, GiantTileSize, MinAltitude, GiantMaxAltitude, GiantVerticalExponent);
		Add(f, GiantOrigin.X, GiantOrigin.Y, LodFineDistance, LodShapeDistance);
		Add(f, Shadow.X, Shadow.Y, Shadow.Z, Shadow.W);
		Add(f, Rain.X, Rain.Y, Rain.Z, Rain.W);
		Add(f, StormEye.X, StormEye.Y, StormEye.Z, StormEye.W);

		var bytes = new byte[ParamsBytes];
		Buffer.BlockCopy(f.ToArray(), 0, bytes, 0, Math.Min(bytes.Length, f.Count * 4));
		_rd.BufferUpdate(_ubo, 0, ParamsBytes, bytes);
		return viewProj;
	}

	private static void Add(List<float> f, float a, float b, float c, float d)
	{
		f.Add(a); f.Add(b); f.Add(c); f.Add(d);
	}

	private static void AddProjection(List<float> f, Projection p)
	{
		Add(f, p.X.X, p.X.Y, p.X.Z, p.X.W);
		Add(f, p.Y.X, p.Y.Y, p.Y.Z, p.Y.W);
		Add(f, p.Z.X, p.Z.Y, p.Z.Z, p.Z.W);
		Add(f, p.W.X, p.W.Y, p.W.Z, p.W.W);
	}

	private static void AddTransform(List<float> f, Transform3D t)
	{
		Add(f, t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, 0f);
		Add(f, t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, 0f);
		Add(f, t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, 0f);
		Add(f, t.Origin.X, t.Origin.Y, t.Origin.Z, 1f);
	}

	private static RDUniform Uniform(RenderingDevice.UniformType type, int binding, params Rid[] ids)
	{
		var u = new RDUniform { UniformType = type, Binding = binding };
		foreach (Rid id in ids)
			u.AddId(id);
		return u;
	}

	// ------------------------------------------------------------------ Bruits 3D tuilables

	/// <summary>
	/// Texture de forme 64³ RGBA8 : R = Perlin-Worley (Perlin FBM remappé dans [Worley, 1]),
	/// G, B, A = Worley FBM aux fréquences croissantes (4, 8, 16 cellules par période).
	/// </summary>
	private static byte[] BuildShapeNoise(int n)
	{
		var bytes = new byte[n * n * n * 4];
		Parallel.For(0, n, z =>
		{
			for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float u = (x + 0.5f) / n, v = (y + 0.5f) / n, w = (z + 0.5f) / n;
				float perlin = CloudNoise.PerlinFbm(u * 4f, v * 4f, w * 4f, 4, 4, 4, 4, 401u);
				float worley4 = CloudNoise.WorleyFbm(u * 4f, v * 4f, w * 4f, 4, 4, 4, 3, 501u);
				float worley8 = CloudNoise.WorleyFbm(u * 8f, v * 8f, w * 8f, 8, 8, 8, 3, 601u);
				float worley16 = CloudNoise.WorleyFbm(u * 16f, v * 16f, w * 16f, 16, 16, 16, 3, 701u);
				// Perlin-Worley : la Perlin "gonflée" par la Worley (forme boursouflée).
				float pw = Mathf.Clamp(Remap(perlin, 0f, 1f, worley4, 1f), 0f, 1f);
				int o = (x + n * (y + n * z)) * 4;
				bytes[o] = ToByte(pw);
				bytes[o + 1] = ToByte(worley4);
				bytes[o + 2] = ToByte(worley8);
				bytes[o + 3] = ToByte(worley16);
			}
		});
		return bytes;
	}

	/// <summary>Texture de détail 32³ RGBA8 : R, G, B = Worley FBM (2, 4, 8 cellules par période).</summary>
	private static byte[] BuildDetailNoise(int n)
	{
		var bytes = new byte[n * n * n * 4];
		Parallel.For(0, n, z =>
		{
			for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float u = (x + 0.5f) / n, v = (y + 0.5f) / n, w = (z + 0.5f) / n;
				int o = (x + n * (y + n * z)) * 4;
				bytes[o] = ToByte(CloudNoise.WorleyFbm(u * 2f, v * 2f, w * 2f, 2, 2, 2, 3, 801u));
				bytes[o + 1] = ToByte(CloudNoise.WorleyFbm(u * 4f, v * 4f, w * 4f, 4, 4, 4, 3, 901u));
				bytes[o + 2] = ToByte(CloudNoise.WorleyFbm(u * 8f, v * 8f, w * 8f, 8, 8, 8, 3, 1001u));
				bytes[o + 3] = 255;
			}
		});
		return bytes;
	}

	private static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / (b - a) * (d - c);

	private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}
