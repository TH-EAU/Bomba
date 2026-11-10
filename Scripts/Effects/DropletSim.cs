using System;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Simulation des gouttes sur l'écran (modèle de L. Bebber, « RainEffect ») : gouttes et gouttelettes de buée dans un
/// repère écran de référence (hauteur 1080 px, y vers le bas).
/// - Une goutte reste collée tant que la force qui la tire (gravité projetée + souffle de l'air) ne vainc pas
///   son adhérence : les petites tiennent, les grosses coulent.
/// - Deux gouttes qui se touchent fusionnent (volume conservé) : en s'accumulant, elles grossissent puis coulent.
/// - Une goutte qui coule ondule, absorbe la buée sur son passage (sillon net) et sème une traîne de gouttelettes.
/// - La buée grossit dans le nuage et finit par former des gouttes.
/// Classe pure, sans nœud : pilotée et dessinée par <see cref="ScreenDroplets"/>.
/// </summary>
public sealed class DropletSim
{
    public const float RefHeight = 1080f;

    public struct Drop
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float R;          // rayon (px de référence)
        public float Age;        // s, pour l'apparition
        public float TrailDist;  // distance parcourue depuis la dernière gouttelette de traîne
        public float Phase;      // méandre propre à la goutte
        public bool Mist;        // gouttelette de buée ou de traîne : ne coule jamais seule
        public bool Sliding;
    }

    // --- Réglages (copiés depuis ScreenDroplets à chaque pas) ---
    public float DropMinRadius = 2.5f, DropMaxRadius = 10f;
    public float MistMinRadius = 0.7f, MistMaxRadius = 1.8f;
    public int MaxDrops = 400, MaxMist = 2500;
    public float StickRadius = 7f;      // rayon qui se met à couler sous une force de 1 (gravité, écran vertical)
    public float SlideSpeed = 140f;     // px/s à force double du seuil
    public float Meander = 0.35f;       // ondulation de la direction (rad)
    public float TrailSpacing = 1.6f;   // distance moyenne entre deux gouttes de traîne (en rayons)
    public float TrailShare = 0.3f;     // taille relative des gouttes de traîne (0,3 = 20 à 45 % du rayon)
    public float GatherTime = 0.5f;     // durée (s) pendant laquelle une goutte écrasée se rassemble avant de pouvoir couler

    /// <summary>Rassemblement après l'impact : 0 = disque écrasé, 1 = goutte bombée.</summary>
    public float Gather(in Drop d) => d.Age >= GatherDuration(d.R) ? 1f : d.Age / GatherDuration(d.R);

    /// <summary>Durée du rassemblement : les grosses gouttes, plus étalées, mettent plus longtemps (0,6 à 1,4 × GatherTime).</summary>
    public float GatherDuration(float r) => Mathf.Max(GatherTime, 0.01f) * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp(r / Mathf.Max(DropMaxRadius, 1f), 0f, 1f));

    public Drop[] Drops = new Drop[4096];
    public int Count;
    public float Width = RefHeight * 16f / 9f;

    private readonly RandomNumberGenerator _rng = new();
    private float _dropAcc, _mistAcc, _time;
    private int _dropCount, _mistCount;

    // Grille de contacts : listes chaînées par cellule.
    private const float Cell = 24f;
    private int _gw, _gh;
    private int[] _head = Array.Empty<int>();
    private int[] _next = new int[4096];

    public DropletSim() => _rng.Randomize();

    public void Clear() => Count = 0;

    /// <param name="force">Gravité projetée sur l'écran (en g, y vers le bas).</param>
    /// <param name="vanish">Point de fuite (px de référence) : le souffle pousse depuis ce point.</param>
    /// <param name="airflow">Force du souffle radial (en g).</param>
    /// <param name="dropRate">Gouttes par seconde.</param>
    /// <param name="mistRate">Gouttelettes de buée par seconde.</param>
    /// <param name="mistGrow">Croissance de la buée (px/s de rayon).</param>
    /// <param name="dry">Séchage (px/s de rayon).</param>
    public void Step(float dt, Vector2 force, Vector2 vanish, float airflow, float dropRate, float mistRate, float mistGrow, float dry)
    {
        if (dt <= 0f)
            return;
        _time += dt;
        Spawn(dt, dropRate, mistRate);
        Move(dt, force, vanish, airflow, mistGrow, dry);
        Merge();
        Compact();
    }

    private void Spawn(float dt, float dropRate, float mistRate)
    {
        _dropAcc += dropRate * dt;
        _mistAcc += mistRate * dt;
        while (_dropAcc >= 1f)
        {
            _dropAcc -= 1f;
            if (_dropCount < MaxDrops)
            {
                // Surtout des petites, quelques grosses (tirage de Bebber : puissance 3).
                float r = Mathf.Lerp(DropMinRadius, DropMaxRadius, Mathf.Pow(_rng.Randf(), 3f));
                Add(RandomPos(), r, false);
            }
        }
        while (_mistAcc >= 1f)
        {
            _mistAcc -= 1f;
            if (_mistCount < MaxMist)
                Add(RandomPos(), Mathf.Lerp(MistMinRadius, MistMaxRadius, _rng.Randf() * _rng.Randf()), true);
        }
    }

    private Vector2 RandomPos() => new(_rng.Randf() * Width, _rng.Randf() * RefHeight);

    private void Add(Vector2 pos, float r, bool mist)
    {
        if (Count >= Drops.Length)
            return;
        Drops[Count++] = new Drop { Pos = pos, R = r, Mist = mist, Phase = _rng.Randf() * 100f };
        if (mist) _mistCount++; else _dropCount++;
    }

    private void Move(float dt, Vector2 force, Vector2 vanish, float airflow, float mistGrow, float dry)
    {
        float stick2 = StickRadius * StickRadius;
        int n = Count;   // les gouttelettes de traîne ajoutées pendant la boucle ne bougent pas ce pas-ci
        for (int i = 0; i < n; i++)
        {
            ref Drop d = ref Drops[i];
            d.Age += dt;

            if (d.Mist)
            {
                d.R += mistGrow * dt - dry * dt;
                if (d.R > DropMinRadius)   // la buée a assez grossi : c'est une goutte
                {
                    d.Mist = false;
                    _mistCount--;
                    _dropCount++;
                }
                continue;
            }
            d.R -= dry * dt;

            // Force : gravité + souffle radial depuis le point de fuite (nul tout près de lui).
            Vector2 away = d.Pos - vanish;
            float dist = away.Length();
            Vector2 f = force;
            if (airflow > 0f && dist > 1f)
                f += away / dist * airflow * Mathf.SmoothStep(0f, 150f, dist);
            float fl = f.Length();

            // Adhérence : la force × volume doit vaincre la ligne de contact (∝ r) ; hystérésis une fois lancée.
            // Une goutte encore écrasée n'a pas de prise : elle se rassemble d'abord.
            float drive = fl * d.R * d.R / stick2 * Gather(d);
            d.Sliding = drive > (d.Sliding ? 0.7f : 1f);
            if (!d.Sliding || fl < 1e-4f)
            {
                d.Vel = Vector2.Zero;
                continue;
            }

            float wobble = Meander * Mathf.Sin(_time * 2.1f + d.Phase) + Meander * 0.6f * Mathf.Sin(_time * 5.3f + d.Phase * 1.7f);
            Vector2 dir = (f / fl).Rotated(wobble);
            // Glisse par à-coups : la goutte s'accroche, repart (adhérence inégale de la vitre).
            float jerk = Mathf.SmoothStep(-0.3f, 0.7f, Mathf.Sin(_time * 3.7f + d.Phase * 3.1f) * 0.6f + Mathf.Sin(_time * 9.1f + d.Phase) * 0.4f);
            float speed = SlideSpeed * Mathf.Clamp(drive - 0.6f, 0f, 3f) * Mathf.Lerp(0.15f, 1f, jerk);
            d.Vel = d.Vel.Lerp(dir * speed, 1f - Mathf.Exp(-8f * dt));
            Vector2 step = d.Vel * dt;
            d.Pos += step;

            // Traîne (Bebber) : une petite goutte (20 à 45 % du rayon) laissée derrière à intervalles irréguliers ;
            // la goutte y perd un peu d'eau.
            d.TrailDist += step.Length();
            if (d.TrailDist > d.R * TrailSpacing * _rng.RandfRange(0.6f, 1.4f) && d.R > DropMinRadius)
            {
                d.TrailDist = 0f;
                float tr = d.R * _rng.RandfRange(0.2f, 0.45f) * TrailShare / 0.3f;
                d.R *= 0.97f;
                Vector2 back = -d.Vel.Normalized() * (d.R * 1.25f + tr);
                Vector2 jitter = new Vector2(-back.Y, back.X).Normalized() * _rng.RandfRange(-0.25f, 0.25f) * d.R;
                Vector2 tp = d.Pos + back + jitter;
                if (_mistCount < MaxMist && Count < Drops.Length)
                {
                    Drops[Count++] = new Drop { Pos = tp, R = tr, Mist = true, Age = 1f, Phase = _rng.Randf() * 100f };
                    _mistCount++;
                }
            }
        }
    }

    /// <summary>Fusions : une goutte absorbe la buée qu'elle touche et fusionne avec les gouttes qu'elle recouvre.</summary>
    private void Merge()
    {
        _gw = Mathf.CeilToInt(Width / Cell) + 1;
        _gh = Mathf.CeilToInt(RefHeight / Cell) + 1;
        if (_head.Length < _gw * _gh)
            _head = new int[_gw * _gh];
        Array.Fill(_head, -1, 0, _gw * _gh);
        if (_next.Length < Drops.Length)
            _next = new int[Drops.Length];

        float maxR = 0f;
        for (int i = 0; i < Count; i++)
        {
            int c = CellOf(Drops[i].Pos);
            if (c < 0) { _next[i] = -1; continue; }
            _next[i] = _head[c];
            _head[c] = i;
            if (!Drops[i].Mist) maxR = Mathf.Max(maxR, Drops[i].R);
        }

        for (int i = 0; i < Count; i++)
        {
            ref Drop a = ref Drops[i];
            if (a.Mist || a.R <= 0f)
                continue;
            int reach = Mathf.CeilToInt((a.R + maxR) / Cell);
            int cx = (int)(a.Pos.X / Cell), cy = (int)(a.Pos.Y / Cell);
            for (int y = Math.Max(cy - reach, 0); y <= Math.Min(cy + reach, _gh - 1); y++)
            for (int x = Math.Max(cx - reach, 0); x <= Math.Min(cx + reach, _gw - 1); x++)
            for (int j = _head[y * _gw + x]; j >= 0; j = _next[j])
            {
                if (j == i)
                    continue;
                ref Drop b = ref Drops[j];
                if (b.R <= 0f)
                    continue;
                float dist = a.Pos.DistanceTo(b.Pos);
                if (b.Mist)
                {
                    if (dist < a.R + b.R * 0.3f)
                        Absorb(ref a, ref b, false);
                }
                else if (dist < (a.R + b.R) * 0.8f)
                {
                    // La plus grosse (ou celle qui coule) garde son élan.
                    if (b.R > a.R && !a.Sliding)
                    {
                        Absorb(ref b, ref a, true);
                        goto next;
                    }
                    Absorb(ref a, ref b, true);
                }
            }
            next:;
        }
    }

    private void Absorb(ref Drop into, ref Drop other, bool movePos)
    {
        float va = into.R * into.R * into.R, vb = other.R * other.R * other.R;
        // Rassemblement de la goutte fusionnée : moyenne pondérée par le volume. Une goutte encore écrasée qui tombe
        // sur une autre ne la fait pas grossir d'un coup : le tout s'étale un peu, puis se rassemble.
        float g = (Gather(into) * va + Gather(other) * vb) / (va + vb);
        if (movePos)
            into.Pos = (into.Pos * va + other.Pos * vb) / (va + vb);
        into.R = Mathf.Pow(va + vb, 1f / 3f);
        if (vb > 0.25f * va)
            into.Age = Mathf.Min(into.Age, GatherDuration(into.R) * g);
        other.R = 0f;
    }

    private int CellOf(Vector2 p)
    {
        int x = (int)(p.X / Cell), y = (int)(p.Y / Cell);
        if (p.X < 0f || p.Y < 0f || x >= _gw || y >= _gh)
            return -1;
        return y * _gw + x;
    }

    /// <summary>Retire les gouttes absorbées, séchées ou sorties de l'écran.</summary>
    private void Compact()
    {
        int w = 0;
        _dropCount = _mistCount = 0;
        float margin = DropMaxRadius * 3f;
        for (int i = 0; i < Count; i++)
        {
            Drop d = Drops[i];
            if (d.R < 0.35f || d.Pos.X < -margin || d.Pos.Y < -margin || d.Pos.X > Width + margin || d.Pos.Y > RefHeight + margin)
                continue;
            Drops[w++] = d;
            if (d.Mist) _mistCount++; else _dropCount++;
        }
        Count = w;
    }
}
