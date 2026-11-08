using System.Linq;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Met en scène les éclairs émis par <see cref="CloudField"/> : traits ramifiés et flash de lumière.
/// Un éclair avec cible frappe cette cible ; sans cible, il peut descendre vers le bas (trait décoratif).
/// </summary>
public partial class LightningSystem : Node3D
{
    /// <summary>Probabilité qu'un éclair décoratif ait un trait visible ; sinon, flash dans le nuage seul.</summary>
    [Export] public float BoltChance { get; set; } = 0.6f;
    [Export] public float MinDownLength { get; set; } = 250f;
    /// <summary>Probabilité qu'un éclair sorti du nuage frappe la mer (nuage-sol).</summary>
    [Export] public float GroundStrikeChance { get; set; } = 0.6f;
    /// <summary>Altitude de la mer (m), cible des éclairs nuage-sol.</summary>
    [Export] public float SeaLevel { get; set; } = -60f;
    [Export] public float MaxDownLength { get; set; } = 6000f;
    [Export] public float FlashEnergy { get; set; } = 16f;
    [Export] public float FlashRange { get; set; } = 8000f;

    private const int MaxActiveBolts = 6;
    private const float DownStep = 40f;
    private const float CloudExitDensity = 0.02f;
    private const float FlashDuration = 0.3f;
    private static readonly Color FlashColor = new(0.7f, 0.8f, 1f);

    private readonly RandomNumberGenerator _rng = new();
    private CloudField _field;

    public override void _Ready()
    {
        _rng.Randomize();
        TryConnectToField();
    }

    public override void _Process(double delta)
    {
        if (TryConnectToField())
            SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (_field != null && IsInstanceValid(_field))
            _field.LightningTriggered -= OnLightningTriggered;
        _field = null;
    }

    private bool TryConnectToField()
    {
        if (_field != null)
            return true;
        if (CloudField.Instance == null)
            return false;
        _field = CloudField.Instance;
        _field.LightningTriggered += OnLightningTriggered;
        return true;
    }

    private void OnLightningTriggered(Vector3 position, Vector3 target, bool hasTarget)
    {
        if (hasTarget)
            AddBolt(position, target);
        else if (_rng.Randf() < BoltChance)
            AddBolt(position, DownwardTarget(position));

        AddFlash(position);
    }

    private void AddBolt(Vector3 from, Vector3 to)
    {
        if (GetChildren().OfType<LightningBolt>().Count() >= MaxActiveBolts)
            return;

        var bolt = new LightningBolt();
        AddChild(bolt);
        bolt.Setup(from, to, _rng);
    }

    private void AddFlash(Vector3 position)
    {
        var light = new OmniLight3D
        {
            LightColor = FlashColor,
            LightEnergy = FlashEnergy,
            OmniRange = FlashRange,
            ShadowEnabled = false,
        };
        AddChild(light);
        light.GlobalPosition = position;

        Tween tween = CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, FlashDuration);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    /// <summary>
    /// Point d'arrivée d'un éclair qui descend : on cherche la sortie du nuage (jusqu'à MaxDownLength sous le flash).
    /// Sous la base, l'éclair frappe souvent la mer (nuage-sol) ; sinon il s'arrête un peu sous la base.
    /// </summary>
    private Vector3 DownwardTarget(Vector3 origin)
    {
        float exit = -1f;
        for (float d = 0f; d < MaxDownLength; d += DownStep)
        {
            if (_field.Sample(origin + Vector3.Down * d).Density <= CloudExitDensity)
            {
                exit = d;
                break;
            }
        }

        float length;
        if (exit < 0f)
            length = _rng.RandfRange(MinDownLength, MaxDownLength * 0.2f);   // reste dans le nuage (flash interne)
        else if (_rng.Randf() < GroundStrikeChance)
            length = Mathf.Max(origin.Y - SeaLevel, exit + 40f);          // nuage-sol : jusqu'à la mer
        else
            length = exit + _rng.RandfRange(40f, 160f);

        Vector2 drift = Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau) * _rng.RandfRange(0f, 0.15f * length);
        return origin + Vector3.Down * length + new Vector3(drift.X, 0f, drift.Y);
    }
}
