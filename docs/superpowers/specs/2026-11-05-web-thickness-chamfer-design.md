# Web thickness-change chamfer (1:4 taper) — design (2026-11-05)

## Why

Where two web plates of different thickness are butt-welded in line, the thicker plate gets a 1:4 taper so its
section steps down smoothly to the thinner one (`CAS.dwt` detail, `CAS.dump.json` `*Model_Space`: one face flush,
the thick plate tapered on the stepped face, dimensions `t` and `4 x t`; the drawing is not to scale, so the label
`4 x t` is the rule). The 3D model currently shows a square step.

## Rules (engineer, 2026-11-05)

- **R1 Scope.** Webs only: `Web`, `ClosedPlate` and `GirderEnd` (GE/GB merged into a web). No flange or faceplate.
- **R2 Joint.** Two web parts are collinear (|cos| ≥ 0.999) and end-to-end (gap or overlap ≤ 5 mm), their
  footprints overlap sideways, and Δt = t_thick − t_thin ≥ **4 mm**. When Δt < 4 there is no chamfer.
- **R3 Taper.** L = **4 × Δt** along the thick web's axis, starting at the joint face. Examples: t10↔t6 gives L = 16,
  t20↔t10 gives L = 40.
- **R4 Side.**
  - The step on each face of the thick plate is measured from the 2D footprints:
    `step± = face±(thick) − face±(thin)`.
  - A face with step ≤ 0.5 mm is flush and is not cut.
  - Every face with step > 0.5 mm is tapered from `step` at the joint to 0 at L. One face flush means one chamfer.
    Centred plates (both faces stepped) means both faces are chamfered, each Δt/2 deep, with the same L.
  - The cut runs through the full web height.

## Expected on 7D-16C (build 2026-11-04 17:01)

There are 10 joints, all with one face flush on the panel-inner side.
- 8 × t10↔t6 with L = 16, e.g. `26965`↔`26964` at Y≈995 and `2696D`↔`26968` at X≈13976. This includes Closed Plate
  `2695A`↔`2696E`.
- 2 × t20↔t10 with L = 40: `269A0` (Web + GE-HP) ↔ `26976`, and `269A1` ↔ `26978`.

## Approaches

- **A (chosen): post-pass boolean.** After the GE/GB merge, subtract a wedge prism from each thick web solid. The
  wedge's plan view is the triangle (joint, thin face) → (joint, thick face) → (L into the thick plate, thick face),
  pushed slightly outward past the thick face, and it is extruded over more than the web height. This is the same
  pattern as the HP-end cut and the CO cutouts, so the 2D classification is untouched.
- B: shape the 2D footprint before extrusion (PlateBuilder). Rejected because it would interfere with the footprint
  segments and the GE split, and touches more code.

## Builder's calls

- The pure geometry (joint detection on 2D centrelines/thickness, steps, wedge polygon) goes in
  `WebThicknessChamferGeometry`, unit tested with the 7D-16C numbers. The AutoCAD-side code is `WebChamferBuilder`.
- It is applied to the final solid of the thick part: for a merged GE/GB, the web it was merged into.
- A failed boolean gives `ctx.Warn` (the web is kept square). The report has one line per joint (parts, t, Δt, L,
  sides).
- There is no Result-tab toggle.
