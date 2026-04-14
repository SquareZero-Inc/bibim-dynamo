# Validation Governance

## Policy Contract
- `PASS`: deliver `TYPE: CODE|...`.
- `WARN`: deliver code and log warnings.
- `BLOCK`: do not deliver code, force `TYPE: VALIDATION_BLOCK|...`.

## Spec Table (7 Axes)
- Symbol existence: runtime reflection, `BLOCK` on unknown symbol.
- Member existence: runtime reflection, `BLOCK` on missing member.
- Signature compatibility: overload arg-count match, `BLOCK` on mismatch.
- Access constraints: static/instance misuse `BLOCK`, unguarded `Parameter.Set` `WARN`.
- Version compatibility: runtime obsolete + XML hints, `WARN`.
- Python runtime: CPython/IronPython incompatible syntax, `BLOCK`.
- Revit usage rules: prohibited patterns (`__revit__`, `SystemExit`, etc.), `BLOCK`.

## Feature Flags
- `validation.gate_enabled`
- `validation.auto_fix_enabled`
- `validation.auto_fix_max_attempts`
- `validation.verify_stage_enabled`
- `validation.enable_api_xml_hints`
- `validation.rollout_phase`

## Rollout Plan
- `phase1`: existence gate only.
- `phase2`: + member/signature/access.
- `phase3`: + runtime/revit/version + autofix loop.

## Ops Review Loop
- Track: `symbols_total`, `block_count`, `warn_count`, `unknown_symbols`, `fix_attempts`, `final_status`.
- Review recurring `blocked_symbols`, then tune prompts/rules/candidates.
