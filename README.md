# CMS-0057-F Prior Authorization — proof of concept

A payer-side Prior Authorization API for a county Behavioral Health Plan, built in C# / .NET to learn what the
CMS Interoperability and Prior Authorization final rule (CMS-0057-F) asks of a plan and how the Da Vinci
implementation guides put it on the wire.

**This is a proof of concept on synthetic data.** It is not a production system, it holds no real member data, and
it is not an official product of any agency. Nothing in it is a statement of policy.

## What it does

A provider system sends a Prior Authorization Request as a Da Vinci PAS Bundle. The service answers at once:

- **Not Required** for services the plan does not prior-authorize (review action `A3`), or
- **Pended** for residential substance use disorder treatment (`A4`), with a reference number and a Decision
  Deadline 24 hours after receipt (DHCS BHIN 24-001).

A Reviewer then approves (`A1`, with an Authorization Number and end date) or denies (`A2`, with a specific
plain-language Denial Reason). The provider system can ask what became of its requests, or subscribe and be told.
An EHR can ask, as a clinician signs an order, whether prior authorization is needed (Da Vinci CRD).

| Endpoint | Guide | Purpose |
| --- | --- | --- |
| `GET /.well-known/smart-configuration`, `POST /auth/token` | SMART App Launch 1.0 Backend Services | Where and how a provider system obtains an access token |
| `POST /fhir/Claim/$submit` | PAS 2.2.1 | Submit a request; answered with a PAS response Bundle |
| `POST /fhir/Claim/$inquire` | PAS 2.2.1 | Ask about the requesting provider's requests for a Member |
| `POST /fhir/Subscription` | PAS 2.2.1 / Subscriptions Backport | Be notified when a request is decided |
| `GET /cds-services`, `POST /cds-services/order-sign-crd` | CRD 2.2.1 | Coverage Information on a draft order |
| `GET /reviewer/pended-requests` | — | What the Reviewer has to decide, nearest deadline first |
| `POST /reviewer/requests/{id}/approve`, `.../deny` | — | The Reviewer's decision |

The wire shapes mirror Da Vinci's `br-payer` reference implementation so that its companion `br-provider` can talk
to this service; see [docs/adr/0001](docs/adr/0001-target-current-da-vinci-guide-versions.md) for the guide
versions chosen and why. The vocabulary the code and tests use is in [GLOSSARY.md](GLOSSARY.md).

## Running it

Prerequisites: the .NET 10 SDK and SQL Server LocalDB (installed with Visual Studio's data workload, or from the
SQL Server Express installer). The database is created and migrated on first start, and three synthetic Members
(`90000001A`, `90000002B`, `90000003C`) are seeded in Development.

```bash
dotnet run --project src/Cms0057.PriorAuthorization
```

The service listens on `http://localhost:5198`. The quickest tour is
[`PriorAuthorization.http`](src/Cms0057.PriorAuthorization/PriorAuthorization.http): open it in Visual Studio or
VS Code (REST Client) and send the requests top to bottom. A sample request Bundle is in
[`samples/`](samples/residential-request.json).

The service's own settings are in [`appsettings.json`](src/Cms0057.PriorAuthorization/appsettings.json): the
connection string, the registered client systems (`Authentication`, below), the rules (`Requirements`: which
billing codes need prior authorization and how long the plan has to decide, standard and expedited) and the plan's
licensed Denial Reason codes. The residential codes (`H0019`
with `U1`/`U2`/`U3`) come from the DMC-ODS billing manual; the mental health codes under BHIN 26-030 are still to be
confirmed against the 2026 manual.

## How a provider system authenticates

The Final Rule requires SMART App Launch 1.0 for the payer APIs, and this service takes the Backend Services
profile: a provider system (or an EHR calling CRD) authenticates as a *system*, with a key pair rather than a
password. Da Vinci's `br-payer` sample runs without authentication; here it is on unless
`Authentication:Enabled` is set to `false` for a local demo.

1. **Registration.** The provider system generates an RSA or EC key pair and gives the plan its client id, the
   scopes it needs and the *public* key, as a JWK Set either inline or at a URL it publishes. The plan enters it
   under `Authentication:Clients` in `appsettings.json`. A sample client, `sample-provider-system`, is registered
   there; its private key is published in
   [`samples/sample-provider-system.private.jwk.json`](samples/sample-provider-system.private.jwk.json) so the
   demo can run. It is synthetic and protects nothing.
2. **Discovery.** `GET /.well-known/smart-configuration` says where the token endpoint is, that the client
   authenticates with `private_key_jwt` signed `RS384` or `ES384`, and which `system/` scopes are offered.
3. **Client assertion.** The system signs a short JWT with its private key: `iss` and `sub` are its client id,
   `aud` is the token endpoint URL, `exp` is at most five minutes ahead and `jti` is unique. The service refuses
   an assertion it has seen before. [`samples/New-ClientAssertion.ps1`](samples/New-ClientAssertion.ps1) signs
   one for the sample client.
4. **Token.** `POST /auth/token` with `grant_type=client_credentials`, the assertion as `client_assertion`
   (type `urn:ietf:params:oauth:client-assertion-type:jwt-bearer`) and the `scope` wanted. The answer is a bearer
   token good for five minutes, carrying the scopes granted. The system then sends `Authorization: Bearer ...` on
   every call to `/fhir/*` and `/cds-services/*`. A missing or expired token is answered 401, a token without the
   scope the operation needs 403, both with an OperationOutcome on `/fhir/*`.

Scopes: `$submit` needs `system/Claim.write`, `$inquire` `system/Claim.read`, a Decision Subscription
`system/Subscription.write`; CRD needs only a token from a registered client. The metrics report under `/reports`
is public, as the rule intends. The Reviewer endpoints under `/reviewer` are for the plan's own staff and are not
covered by SMART Backend Services; their protection is a separate concern, tracked in the issues. Access tokens are
signed with a key generated when the service starts, so they do not survive a restart.

[`PriorAuthorization.http`](src/Cms0057.PriorAuthorization/PriorAuthorization.http) walks through it: step A
trades a pasted assertion for a token and the later steps carry that token.

## Tests

```bash
dotnet test
```

The tests drive the service over HTTP only, each with a throwaway LocalDB database and a fake clock. They are
written as the behaviour a provider system or a Reviewer would see, and read best as a specification:
[tests/](tests/Cms0057.PriorAuthorization.Tests).

## What is and is not built

Built: PAS submit, inquire and subscription notifications; CRD order-sign; configurable rules with standard and
expedited deadlines (24 hours for residential treatment; 5 business days / 72 hours for the BHIN 26-030 mental
health services); several items per request; updates, cancellations and renewals; approval and denial with the
rule's required content, including coded Denial Reasons; overdue tracking; the public metrics report; SMART
Backend Services authentication for the provider-facing endpoints.

Not built, tracked as [issues](https://github.com/tngo0508/CMS-0057-F/issues): DTR questionnaires, a Reviewer
screen and the Reviewer endpoints' own authentication, FHIR profile validation, and the rule's other three APIs (Patient Access,
Provider Access, Payer-to-Payer). Several issues are questions for the agency rather than work items.

## Sources and attribution

- CMS-0057-F final rule and fact sheet; DHCS BHIN 24-001, 26-008 and 26-030.
- HL7 Da Vinci PAS 2.2.1, CRD 2.2.1, DTR 2.2.0; FHIR R4.0.1; Subscriptions Backport; SMART App Launch 1.0.0
  Backend Services.
- The request and inquiry Bundles used in the tests and samples are adapted from the examples in Da Vinci's
  [`br-payer`](https://github.com/HL7-DaVinci/br-payer) reference implementation (Apache License 2.0), cut down and
  re-coded for behavioral health services. The FHIR model and serialisation come from the
  [Firely .NET SDK](https://github.com/FirelyTeam/firely-net-sdk).

## Licence

[MIT](LICENSE). The request examples adapted from `br-payer` remain under their Apache License 2.0, as noted
above.
