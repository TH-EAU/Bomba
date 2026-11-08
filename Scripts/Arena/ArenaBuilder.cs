using Bomba.Core;
using Godot;

namespace Bomba.Arena;

/// <summary>
/// Construit en code l'arène de test : ciel, mer, zone de mort, îlots flottants et anneaux de parcours.
/// Les îlots sont tirés avec <see cref="Seed"/> : une même graine donne toujours la même arène.
/// Script outil : l'arène est aussi construite dans l'éditeur pour l'aperçu. Les nœuds générés
/// (sous "_Generated", sans propriétaire) ne sont jamais enregistrés dans la scène.
/// </summary>
[Tool]
public partial class ArenaBuilder : Node3D
{
    private const string GeneratedName = "_Generated";
    private Node3D _root;

    /// <summary>Graine du générateur d'îlots.</summary>
    [Export] public int Seed { get; set; } = 1337;

    /// <summary>Altitude de la mer (m). La zone de mort y est placée.</summary>
    [Export] public float SeaLevel { get; set; } = -60f;

    /// <summary>Nombre d'îlots.</summary>
    [Export] public int IslandCount { get; set; } = 40;

    /// <summary>Distance maximale des îlots au centre (m).</summary>
    [Export] public float ArenaRadius { get; set; } = 1600f;

    /// <summary>Distance minimale des îlots au centre (m), pour dégager la zone d'apparition.</summary>
    [Export] public float SpawnClearRadius { get; set; } = 220f;

    /// <summary>Nombre d'anneaux de parcours.</summary>
    [Export] public int RingCount { get; set; } = 12;

    /// <summary>Rayon du cercle sur lequel sont posés les anneaux (m).</summary>
    [Export] public float RingCourseRadius { get; set; } = 260f;

    /// <summary>Nombre de mines flottantes.</summary>
    [Export] public int MineCount { get; set; } = 14;

    /// <summary>Nombre de zones de rafale.</summary>
    [Export] public int WindZoneCount { get; set; } = 5;

    public override void _Ready() => Rebuild();

    /// <summary>Détruit et reconstruit tout le contenu généré.</summary>
    public void Rebuild()
    {
        Node old = GetNodeOrNull(GeneratedName);
        if (old != null)
        {
            RemoveChild(old);
            old.QueueFree();
        }
        _root = new Node3D { Name = GeneratedName };
        AddChild(_root);

        BuildEnvironment();
        BuildSea();
        _root.AddChild(new KillZone { Position = new Vector3(0f, SeaLevel, 0f) });
        BuildIslands();
        BuildRings();
        BuildMines();
        BuildWindZones();
    }

    private void BuildEnvironment()
    {
        var sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Sky/sky_dusk.gdshader") };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 1.2f,
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.62f, 0.85f),
            FogDensity = 0.000015f,
            FogSkyAffect = 0.15f,
            FogAerialPerspective = 0.5f,
        };
        _root.AddChild(new WorldEnvironment { Environment = environment });

        // Crépuscule (soleil couché, ombre de la Terre) : paramètres partagés via Twilight.
        Vector2 az = Twilight.SunAzimuth.Normalized();
        sky.SetShaderParameter("sun_azimuth", az);
        sky.SetShaderParameter("sun_depression", Twilight.Depression);
        sky.SetShaderParameter("earth_radius", Twilight.EarthRadius);

        // Soleil rasant : n'éclaire que les véhicules au-dessus de l'ombre (voir TwilightSun).
        _root.AddChild(new TwilightSun());
    }

    private void BuildSea()
    {
        _root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(300000f, 300000f), SubdivideWidth = 0, SubdivideDepth = 0 },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Ocean/ocean.gdshader") },
            Position = new Vector3(0f, SeaLevel, 0f),
        });
    }

    private void BuildIslands()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)Seed;

        for (int i = 0; i < IslandCount; i++)
        {
            float distance = rng.RandfRange(SpawnClearRadius, ArenaRadius);
            float angle = rng.RandfRange(0f, Mathf.Tau);
            float y = rng.RandfRange(-10f, 160f);
            float radius = rng.RandfRange(12f, 45f);

            var position = new Vector3(distance * Mathf.Cos(angle), y, distance * Mathf.Sin(angle));
            _root.AddChild(CreateIsland(radius, position));
        }
    }

    /// <summary>Îlot : plateau rocheux sphérique, pointe dessous, dessus d'herbe sans collision.</summary>
    private static StaticBody3D CreateIsland(float r, Vector3 position)
    {
        var island = new StaticBody3D { Position = position };
        var rock = new Color(0.12f, 0.13f, 0.18f);

        var plateau = new SphereMesh { Radius = r, Height = r * 0.7f };
        island.AddChild(new MeshInstance3D { Mesh = plateau, MaterialOverride = new StandardMaterial3D { AlbedoColor = rock, Roughness = 0.9f } });
        island.AddChild(new CollisionShape3D { Shape = plateau.CreateConvexShape() });

        var pointe = new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = 0.5f, Height = r * 1.4f };
        var pointeOffset = new Vector3(0f, -r * 0.7f, 0f);
        island.AddChild(new MeshInstance3D { Mesh = pointe, MaterialOverride = new StandardMaterial3D { AlbedoColor = rock, Roughness = 0.9f }, Position = pointeOffset });
        island.AddChild(new CollisionShape3D { Shape = pointe.CreateConvexShape(), Position = pointeOffset });

        var herbe = new CylinderMesh { TopRadius = r * 0.85f, BottomRadius = r * 0.85f, Height = 0.6f };
        island.AddChild(new MeshInstance3D
        {
            Mesh = herbe,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.17f, 0.17f) },
            Position = new Vector3(0f, r * 0.33f, 0f),
        });

        return island;
    }

    /// <summary>Mines flottantes entre 120 et 500 m de l'origine, altitude 20 à 110 m.</summary>
    private void BuildMines()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Seed + 1);

        for (int i = 0; i < MineCount; i++)
        {
            float distance = rng.RandfRange(120f, 500f);
            float angle = rng.RandfRange(0f, Mathf.Tau);
            float y = rng.RandfRange(20f, 110f);

            var position = new Vector3(distance * Mathf.Cos(angle), y, distance * Mathf.Sin(angle));
            _root.AddChild(new FloatingMine { Position = position });
        }
    }

    /// <summary>Zones de rafale entre 150 et 550 m de l'origine : la première moitié en colonnes ascendantes, le reste en vent latéral.</summary>
    private void BuildWindZones()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Seed + 2);

        for (int i = 0; i < WindZoneCount; i++)
        {
            float distance = rng.RandfRange(150f, 550f);
            float angle = rng.RandfRange(0f, Mathf.Tau);
            float y = rng.RandfRange(30f, 120f);
            float radius = rng.RandfRange(35f, 60f);

            Vector3 windVelocity;
            if (i < WindZoneCount / 2)
            {
                windVelocity = new Vector3(0f, 14f, 0f);
            }
            else
            {
                float windAngle = rng.RandfRange(0f, Mathf.Tau);
                windVelocity = new Vector3(15f * Mathf.Cos(windAngle), 0f, 15f * Mathf.Sin(windAngle));
            }

            var position = new Vector3(distance * Mathf.Cos(angle), y, distance * Mathf.Sin(angle));
            _root.AddChild(new WindZone { Position = position, Radius = radius, WindVelocity = windVelocity });
        }
    }

    /// <summary>Anneaux répartis sur un cercle, axe de passage Z suivant la tangente au cercle.</summary>
    private void BuildRings()
    {
        for (int i = 0; i < RingCount; i++)
        {
            float angle = i / (float)RingCount * Mathf.Tau;
            float y = 60f + 35f * Mathf.Sin(angle * 2f);
            var position = new Vector3(RingCourseRadius * Mathf.Cos(angle), y, RingCourseRadius * Mathf.Sin(angle));

            // Tangente au cercle dans le sens de parcours (dérivée de la position par rapport à l'angle).
            var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            var right = Vector3.Up.Cross(tangent).Normalized();
            var basis = new Basis(right, Vector3.Up, tangent);

            _root.AddChild(new Ring { Transform = new Transform3D(basis, position) });
        }
    }
}
