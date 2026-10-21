"""Fit RadarCalculator weights from the --calibrate-radar CSV.

usage: radar_fit.py features.csv [out.txt] [--level] [--no-transforms] [--maxk N]

Per axis: forward stepwise selection of features (raw, sqrt:, log1p:) by 5-fold
cross-validated R² on the training charts (id % 5 != 0), then a final OLS fit on all
training charts. Reports train / CV / held-out (id % 5 == 0) R² and prints the C#
weight tables for RadarCalculator. Pure Python: all fits go through per-partition
Gram matrices, so adding a candidate costs a k×k solve instead of a pass over rows.
"""
import csv, math, sys
from operator import mul

args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = [a for a in sys.argv[1:] if a.startswith('--')]
path = args[0]
out_path = args[1] if len(args) > 1 else None
USE_LEVEL = '--level' in flags
TRANSFORMS = '--no-transforms' not in flags
MAXK = int(flags[flags.index('--maxk') + 1]) if '--maxk' in flags else 14
MIN_GAIN = 0.002
AXES = ['notes', 'peak', 'tsumami', 'tricky', 'hand-trip', 'one-hand']
CLAMP = 255
# Camera work correlates with intensity in Konami's charts but is a charting-style proxy;
# with this flag only TRICKY (where it is part of the definition) may use camera features.
CAMERA_ONLY_TRICKY = '--camera-only-tricky' in flags


def is_camera(name):
    b = name.split(':', 1)[-1]
    return b.startswith('cam_') or b.startswith('tilt_modes') or b.startswith('lane_clears')

rows = list(csv.DictReader(open(path, encoding='utf-8')))
n_all = len(rows)
base = [k for k in rows[0] if not k.endswith('_actual') and k not in ('id', 'ascii', 'diff')]
if not USE_LEVEL:
    base = [b for b in base if b != 'level']

# --- candidate columns ------------------------------------------------------------
cands = {}
for b in base:
    v = [float(r[b]) for r in rows]
    if max(v) == min(v):
        continue
    cands[b] = v
    if TRANSFORMS and min(v) >= 0:
        cands['sqrt:' + b] = [math.sqrt(x) for x in v]
        cands['log1p:' + b] = [math.log1p(x) for x in v]
names = ['intercept'] + sorted(cands)
scale = [1.0] + [math.sqrt(sum(map(mul, cands[nm], cands[nm])) / n_all) or 1.0 for nm in names[1:]]
cols = [[1.0] * n_all] + [[x / s for x in cands[nm]] for nm, s in zip(names[1:], scale[1:])]
p = len(names)
targets = {a: [float(r[a + '_actual']) for r in rows] for a in AXES}
print(f'{n_all} charts, {p - 1} candidate columns ({len(base)} base features)')

# --- partitions: held-out test + 5 training folds grouped by song id ----------------
part_of = []
for r in rows:
    i = int(r['id'])
    part_of.append('test' if i % 5 == 0 else 'f%d' % ((i // 5) % 5))
parts = {}
for idx, pt in enumerate(part_of):
    parts.setdefault(pt, []).append(idx)
folds = sorted(k for k in parts if k != 'test')


def gram(idx):
    pc = [[c[i] for i in idx] for c in cols]
    G = [[0.0] * p for _ in range(p)]
    for a in range(p):
        ca = pc[a]
        for b in range(a, p):
            G[a][b] = G[b][a] = sum(map(mul, ca, pc[b]))
    stats = {}
    for ax in AXES:
        y = [targets[ax][i] for i in idx]
        stats[ax] = ([sum(map(mul, c, y)) for c in pc], sum(map(mul, y, y)), sum(y), len(y))
    return G, stats


print('building Gram matrices...', flush=True)
grams = {k: gram(v) for k, v in parts.items()}
G_train = [[sum(grams[f][0][a][b] for f in folds) for b in range(p)] for a in range(p)]


def train_stats(ax, exclude=None):
    xty = [sum(grams[f][1][ax][0][i] for f in folds if f != exclude) for i in range(p)]
    yy = sum(grams[f][1][ax][1] for f in folds if f != exclude)
    sy = sum(grams[f][1][ax][2] for f in folds if f != exclude)
    n = sum(grams[f][1][ax][3] for f in folds if f != exclude)
    return xty, yy, sy, n


def solve(A, b):
    n = len(b)
    M = [A[i][:] + [b[i]] for i in range(n)]
    for c in range(n):
        piv = max(range(c, n), key=lambda r: abs(M[r][c]))
        if abs(M[piv][c]) < 1e-10:
            return None
        M[c], M[piv] = M[piv], M[c]
        for r in range(n):
            if r != c and M[r][c] != 0.0:
                f = M[r][c] / M[c][c]
                M[r] = [x - f * y for x, y in zip(M[r], M[c])]
    return [M[i][n] / M[i][i] for i in range(n)]


def fit(S, G, xty):
    return solve([[G[i][j] for j in S] for i in S], [xty[i] for i in S])


def sse(w, S, G, xty, yy):
    return yy - 2 * sum(wi * xty[i] for wi, i in zip(w, S)) + sum(wi * wj * G[i][j] for wi, i in zip(w, S) for wj, j in zip(w, S))


fold_cache = {}


def cv_r2(ax, S):
    tot_sse = tot_sst = 0.0
    for f in folds:
        key = (ax, f)
        if key not in fold_cache:
            fold_cache[key] = train_stats(ax, exclude=f)
        xty, yy, sy, n = fold_cache[key]
        Gf = grams[f][0]
        G = [[G_train[i][j] - Gf[i][j] for j in S] for i in S]
        w = solve(G, [xty[i] for i in S])
        if w is None:
            return -1.0
        fx, fyy, fsy, fn = grams[f][1][ax]
        tot_sse += sse(w, S, Gf, fx, fyy)
        tot_sst += fyy - fsy * fsy / fn
    return 1 - tot_sse / tot_sst


def row_r2(ax, w, S, idx):
    y = [targets[ax][i] for i in idx]
    pred = [min(CLAMP, max(0.0, sum(wi * cols[j][i] for wi, j in zip(w, S)))) for i in idx]
    m = sum(y) / len(y)
    ss = sum((a - b) ** 2 for a, b in zip(y, pred))
    st = sum((a - m) ** 2 for a in y)
    return 1 - ss / st, math.sqrt(ss / len(y))


report = []
for ax in AXES:
    xty_all, yy_all, sy_all, n_tr = train_stats(ax)
    S = [0]
    best = cv_r2(ax, S)
    allowed = [j for j in range(1, p) if not (CAMERA_ONLY_TRICKY and ax != 'tricky' and is_camera(names[j]))]
    while len(S) <= MAXK:
        cand_best, cand_score = None, best
        for j in allowed:
            if j in S:
                continue
            sc = cv_r2(ax, S + [j])
            if sc > cand_score:
                cand_best, cand_score = j, sc
        if cand_best is None or cand_score - best < MIN_GAIN:
            break
        S.append(cand_best)
        best = cand_score
        print(f'  {ax:9s} +{names[cand_best]:32s} cv R²={best:.4f}', flush=True)

    w = fit(S, G_train, xty_all)
    tr = row_r2(ax, w, S, [i for f in folds for i in parts[f]])
    te = row_r2(ax, w, S, parts['test'])
    line = f'{ax:10s} k={len(S) - 1:2d}  train R²={tr[0]:.3f}  cv R²={best:.3f}  held-out R²={te[0]:.3f} rmse={te[1]:.1f}'
    print(line)
    report.append(line)
    report.append('        private static readonly Dictionary<string, double> %sWeights = new Dictionary<string, double>' %
                  {'notes': 'Notes', 'peak': 'Peak', 'tsumami': 'Tsumami', 'tricky': 'Tricky', 'hand-trip': 'HandTrip', 'one-hand': 'OneHand'}[ax])
    report.append('        {')
    for wi, j in zip(w, S):
        report.append('            { "%s", %.6f },' % (names[j], wi / scale[j]))
    report.append('        };')
    report.append('')

print()
print('\n'.join(report))
if out_path:
    open(out_path, 'w', encoding='utf-8').write('\n'.join(report) + '\n')
