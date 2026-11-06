# Target the Da Vinci guide versions the Reference Implementation speaks, not the ones the Final Rule names

The Final Rule recommends PAS 2.0.1, CRD 2.0.1 and DTR 2.0.0, but Da Vinci's current payer Reference Implementation
(`br-payer`, read November 2026) targets PAS 2.2.1, CRD 2.2.1 and DTR 2.2.0. We target the 2.2.x versions, because the
rule only recommends its versions and Da Vinci's provider-side sample is our test counterpart, so we have to speak what
it speaks.

## Consequences

Anyone comparing this service with the Final Rule's list of guides will find a mismatch; it is deliberate. A test kit or
trading partner that only speaks 2.0.x may not interoperate without changes.
