using Godot;

namespace Bomba.Vehicles;

/// <summary>
/// Base de tout module physique (propulseur, hélice, ballon, voile, surface aéro…).
///
/// Un module s'attache au premier <see cref="RigidBody3D"/> ancêtre (le "corps").
/// Si ce corps est un <see cref="Vehicle"/>, le module lit ses commandes et son état ;
/// sinon (ex. mine flottante) il fonctionne en autonome : seul le canal Always vaut 1.
///
/// Chaque frame physique, le module calcule sa force dans <see cref="Simulate"/> et
/// l'applique À SA PROPRE POSITION via <see cref="ApplyForceAtSelf"/> : le couple
/// émerge naturellement de son placement sur le corps.
///
/// Ordre d'exécution : le Vehicle (parent) s'exécute avant ses modules (enfants),
/// donc les commandes et l'état du véhicule sont à jour quand Simulate est appelé.
/// </summary>
[Tool]
public abstract partial class VehicleModule : Node3D
{
    private const string VisualName = "_Visual";
    private Node3D _visual;

    /// <summary>
    /// Racine des visuels générés en code. Recréée au premier accès (un ancien "_Visual" est supprimé),
    /// ce qui évite les doublons dans l'éditeur. Sans propriétaire : jamais enregistrée dans la scène.
    /// </summary>
    protected Node3D Visual
    {
        get
        {
            if (_visual != null && IsInstanceValid(_visual))
                return _visual;
            Node old = GetNodeOrNull(VisualName);
            if (old != null)
            {
                RemoveChild(old);
                old.QueueFree();
            }
            _visual = new Node3D { Name = VisualName };
            AddChild(_visual);
            return _visual;
        }
    }

    /// <summary>Canal d'entrée qui pilote ce module.</summary>
    [Export] public InputChannel Channel { get; set; } = InputChannel.Throttle;

    /// <summary>Module actif. Désactiver un module permet de tester l'effet de son placement.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>Corps physique qui porte ce module.</summary>
    public RigidBody3D Body { get; private set; }

    /// <summary>Véhicule qui porte ce module, ou null si le corps n'est pas un Vehicle.</summary>
    public Vehicle Vehicle { get; private set; }

    /// <summary>Somme des forces (globales) appliquées par ce module lors de la dernière frame. Pour le debug.</summary>
    public Vector3 LastForce { get; private set; }

    /// <summary>Poussée maximale fournie par ce module s'il est un moteur (sinon 0). Sert au PowerRatio.</summary>
    public virtual float MaxPower => 0f;

    /// <summary>Poussée actuelle fournie par ce module s'il est un moteur (sinon 0). Sert au PowerRatio.</summary>
    public virtual float CurrentPower => 0f;

    public override void _Ready()
    {
        Node node = GetParent();
        while (node != null && node is not RigidBody3D)
            node = node.GetParent();

        Body = node as RigidBody3D;
        Vehicle = Body as Vehicle;

        if (Body == null && !Engine.IsEditorHint())
            GD.PushWarning($"{Name}: aucun RigidBody3D parent, le module sera inactif.");
    }

    public override void _PhysicsProcess(double delta)
    {
        LastForce = Vector3.Zero;
        if (Engine.IsEditorHint() || !Enabled || Body == null || Body.Freeze)
            return;

        float command = Vehicle != null
            ? Vehicle.CurrentInput.GetChannel(Channel)
            : (Channel == InputChannel.Always ? 1f : 0f);

        Simulate((float)delta, command);
    }

    /// <summary>
    /// Calcule et applique les forces du module pour cette frame.
    /// </summary>
    /// <param name="delta">Durée de la frame physique (s).</param>
    /// <param name="command">Valeur du canal d'entrée du module (0..1 ou -1..1 selon le canal).</param>
    protected abstract void Simulate(float delta, float command);

    /// <summary>Applique une force globale (N) à la position du module.</summary>
    protected void ApplyForceAtSelf(Vector3 globalForce)
    {
        Body.ApplyForce(globalForce, GlobalPosition - Body.GlobalPosition);
        LastForce += globalForce;
    }

    /// <summary>Vitesse (globale) du point du corps où se trouve ce module, rotation comprise.</summary>
    protected Vector3 GetPointVelocity()
    {
        Vector3 r = GlobalPosition - Body.GlobalPosition;
        return Body.LinearVelocity + Body.AngularVelocity.Cross(r);
    }
}
