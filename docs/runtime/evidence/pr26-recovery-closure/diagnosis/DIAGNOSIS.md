# Architecture-quality Recovery02 failure diagnosis

Original failure preserved, never rerun to overwrite its result. The retained failure log exits 134 with one failed assertion, while all later raw/image custody assertions pass.

## Evidence and conclusion

`original-failure-facts.json` records the original log/database SHA-256 and read-only database observations: no `QueryInconclusive` audit and only one upload Query, which is the later successful original-outcome recovery. Thus the first command never entered the original-query receipt path. Its exact returned status was not logged by the original test.

`Program.cs` runs the pinned PR26 real reference Application/Assembly runtime with a controlled WCS health completion and counting empty upload Query. `results.log` proves: health in flight => externalWaits 1, Configure false, ReconcileReceipt Rejected, Query calls 0; after health drains and public Configure is observed => Unresolved, Query calls 1, original Submit Awaiting without raw, new Query NoReply, original image retained.

The relevant guard, health, recovery and fixture source files are identical between pinned PR26 head 78298b5dc7ccdcb7bb08e857d2d94f3681182fde and latest-main baseline baac260214f910dca9ae4628ba6449b25188e2ac. This reproduces the missing precondition deterministically rather than weakening the production fence.

## Test correction

Keep the production external-wait fence and the original Unresolved/image assertion. First force and assert the in-flight rejection with unchanged custody and no Query; release the controlled health request; observe public Configure; execute the explicit original Query and assert exactly one call plus the existing outcome/custody assertions. This is a reference runtime validation, not Windows/SDK/protocol/field acceptance.

## Diagnostic environment failure retained

`initial-native-resolution-failure.log` and matching runtime directory retain the first standalone probe setup failure (the copied managed references did not resolve SQLite native library). Copying the pinned build's macOS arm64 libe_sqlite3.dylib into the standalone probe output corrected only its diagnostic packaging. The subsequent proof passed; the historical architecture-quality failure remains FAIL.
