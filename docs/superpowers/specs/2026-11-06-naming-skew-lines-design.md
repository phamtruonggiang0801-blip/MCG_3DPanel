# Naming: skew Web, HP and Flange lines — design (2026-11-06)

## Why

Panel 7D-17C (CAS.dump.json, block `7D-17C_Structure`) has lines running 27° off the Y axis. The Naming tab compares
each piece's mid-point along one axis, which only works for axis-aligned members, so one skew girder is reported as
several lines with the wrong flange size. The names feed the 2D drawings, so they must be right without manual Merge.

## What the dump shows

| Skew line | Pieces | Collinearity |
|---|---|---|
| Web | 1ADF9, 1AE23, 1AE5C, 1AE5D (t = 6 / 10), about 22.5 m | within 3 mm |
| Flange | 5 plates 150 wide, plus 5 junction plates 447–615 wide | 150 plates within 1 mm |
| HP | 1AE8A, 1AE8B, 1AE8C, 190 mm beside the web | within 0.3 mm |

Current logic, simulated from the dump (AM_0 = Web, AM_3 = HP, AM_11 = Flange assumed):

- The web becomes T2, T4 and T7; the HP becomes 3 lines.
- Flange size reads 447, 579 and 80 instead of 150. T7 borrows 80 from a straight web.
- Straight webs T1 and T6 highlight junction plates of the skew line.
- T6 and T7 centres are 40 mm apart. Under 15 mm, a straight and a skew web would merge.
- The 2026-11-06 decision "number by the centre of the whole line" is not in the code: `Center` is the longest piece's.

## Goals and non-goals

- Goal: collinear skew pieces form one line, with the correct flange size and highlight.
- Goal: panels with no skew member give exactly the same result as today.
- Non-goal: a separate D# series (rejected by the engineer).
- Non-goal: filtering short stray pieces. Del Name stays the answer, including HP 1AF68 / 1AF69.
- Non-goal: changing classification.

## Approaches

- **1. Line test by angle and perpendicular distance, inside StructureNaming (chosen).** Fixes grouping, numbering and flanges.
- 2. Manual Merge only. Rejected: it leaves the flange wrong and repeats the work on every panel.
- 3. Separate D# series. Rejected by the engineer.

## Design

All in `Naming/StructureNaming.cs` (pure C#).

- **Skew piece:** long axis more than 1° from X and from Y. Pieces within 1° of an axis keep today's code path.
- **Direction:** dominant axis, as today (exactly 45° goes to L).
- **Grouping:** skew pieces of one group (Web + HP, or FB) join a line when their angles differ by ≤ 1° and their
  perpendicular distance is ≤ `LINE_TOL` (15 mm). They need not touch. A skew piece never joins an axis-aligned one.
- **Line kind and Size:** unchanged — larger total length; Size from the longest piece (mixed t = 6 / 10 is one line).
- **Numbering:** a skew line uses the midpoint of the whole line's extent across its direction (X for T, Y for L).
  It stays in the T / L / HT / HL series, so later numbers shift.
- **Flange on a skew web:** parallel within 1°, perpendicular distance ≤ half flange width + `LINE_TOL`, and
  overlapping along the web axis. A skew flange never attaches to an axis-aligned web, and the reverse.
- **Junction plates:** attach only to the line they are parallel to, for Size and highlight (my call).

Expected on 7D-17C, assuming the classification above: the web line centres at X ≈ 5388 and reads
`T3 WEB 374x6.0/FLANGE 150x20.0`; straight webs after it shift up (6297 = T4 … 15404 = T9); the HP is one HT line at
X ≈ 5246.

Existing user names stay. Auto numbers may shift. Stored Merge groups on skew pieces become no-ops and cause no error.

## Left to the builder

- Internal helper shape. Tests: a 7D-17C fixture from the dump, and proof the 7D-16C fixture is unchanged.
- Update the structure-naming skill and SESSION_LOG.
- Classification (for example 1ADF9) is checked by the engineer in AutoCAD.
