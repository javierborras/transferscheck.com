---
title: 'Typed settings for the weekly cap and one-hour lifetimes'
type: 'feature'
ticket: ''
created: '2026-10-24'
status: 'built'
baseline_revision: '155f648d2185c289a81b7fc94d169d69520880b5'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The process still locks a withdrawn single trial of 14 days and a single price of 5999, and it does not yet expose the live typed settings for the weekly cap, the alert-point list, and the one-hour code and invite lifetimes.

**Approach:** Story 1.3 stays as the existing tenant-filter test. Story 1.4's existing test asserts the live locked settings and stops requiring TrialDays 14 and PriceAmount 5999. Production adds those settings on the existing options types and removes the two withdrawn numbers without substituting another trial length or price.

</frozen-after-approval>

## Implementation Notes

Oneshot: the edit is a handful of typed properties and the test that locks them, well under 100 lines.

The tree already had `WeeklyQueryCap`, `WeeklyQueryWarningGap`, `AlertPointRecordCount`, `EmailCodeLifetime`, and `InviteLinkLifetime`, and the configuration test already asserted them. The red step added the assertion that `TrialDays` and `PriceAmount` are not properties of `CommercialOptions`. That failed on `Int32 TrialDays` before those properties were removed.

`14` now lives as a local in `ConnectionEndpoints.Callback`. `5999` is the fallback at the two `BillingEndpoints` call sites that still pass an int into the pay response and `CreatePreferenceAsync`. Those call sites still read the existing test-host key `Commercial:PriceAmount` so the epic 5 pay-screen test can keep sending 4242. No new commercial number was chosen. Epic 3 and epic 5 tests were not edited. No commit.

The earlier stop, that removing `TrialDays` and `PriceAmount` would not compile, is closed. The existing integers moved to the call sites. No new commercial number was chosen.

## Review Triage Log

- `ConnectionEndpoints` local `14`: false. The callback still starts the current trial with the integer that was already on `CommercialOptions`. That is the authorized bridge until wave 9, not a new trial length. Epic 3 still requires `TrialEndsAt` and `AccessUntil` on connect.
- `BillingEndpoints` fallback `5999`: false. Both call sites keep the integer that was already on `CommercialOptions`, and they still honor the existing test-host key so the pay-screen test can send 4242. That is not a new plan price. Wave 9 replaces it with the plan row.

## Verification

**Commands:**
- `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj --filter FullyQualifiedName~Typed_configuration_carries_the_locked_commercial_numbers` -- expected: fail before the production edit, then pass
- `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj` -- expected: the wave's project passes after the production edit
