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
An EHR can ask, as a clinician signs an order, whether prior authorization is needed and which documentation goes
with it (Da Vinci CRD), and its DTR app can then fetch that Documentation Questionnaire, started, to complete
(Da Vinci DTR), as a whole or a few questions at a time. The agency's Patient Access API reads each Member's Prior
Authorization Information here, in the shape Da Vinci PDex gives it, to show the Member.

| Endpoint | Guide | Purpose |
| --- | --- | --- |
| `GET /.well-known/smart-configuration`, `POST /auth/token` | SMART App Launch 1.0 Backend Services | Where and how a provider system obtains an access token |
| `POST /fhir/Claim/$submit` | PAS 2.2.1 | Submit a request; answered with a PAS response Bundle |
| `POST /fhir/Claim/$inquire` | PAS 2.2.1 | Ask about the requesting provider's requests for a Member |
| `GET /fhir/ClaimResponse/{id}` | PAS 2.2.1 | Read one response by the id it carries (what a notification points at) |
| `POST /fhir/Subscription`, `GET /fhir/Subscription?url=&status=` | PAS 2.2.1 / Subscriptions Backport | Be notified when a request is decided; look up the subscription already held |
| `GET /cds-services`, `POST /cds-services/order-sign-crd` | CRD 2.2.1 | Coverage Information on a draft order: whether prior authorization is needed and the questionnaire to complete |
| `POST /fhir/Questionnaire/$questionnaire-package` | DTR 2.2.0 | The Documentation Questionnaire(s) for an order or by canonical, each with a started QuestionnaireResponse |
| `POST /fhir/Questionnaire/$next-question` | DTR 2.2.0 / SDC adaptive forms | The next questions of an Adaptive Questionnaire, chosen from the answers so far |
| `GET /fhir/ExplanationOfBenefit?patient=&_lastUpdated=`, `GET /fhir/ExplanationOfBenefit/{id}` | PDex 2.1.0 | A Member's Prior Authorization Information, one ExplanationOfBenefit per request: for the Patient Access API to serve on, or for a provider the Member is attributed to |
| `GET /fhir/Group`, `GET /fhir/Group/{id}` | PDex 2.1.0 / ATR 2.1.0 | A provider system's Attribution List: the Members it may see |
| `GET`/`POST /fhir/Group/{id}/$export`, `GET /fhir/$export-status/{job}`, `GET /fhir/$export-files/{job}/{type}.ndjson` | Bulk Data 1.0 | Export the list's Prior Authorization Information (Provider Access API) |
| `PUT /members/{id}/provider-access`, `POST /members/{id}/attributions` | — | Staff: a Member's Opt-Out, and an Attribution the plan records itself |
| `POST /fhir/Patient/$member-match` | HRex 1.1.0 | Another payer finds a Member who has opted in (Payer-to-Payer API); the matched Members are its `Group`, with `$export` as above |
| `GET /fhir/Patient/{id}/$everything` | PDex 2.1.0 | Everything shared with another payer about a matched Member |
| `PUT /members/{id}/payer-to-payer` | — | Staff: a Member's Opt-In to the Payer-to-Payer exchange |
| `GET /reviewer` | — | The Reviewer's page: the Pended requests, and the approve/deny forms |
| `GET /reviewer/pended-requests` | — | What the Reviewer has to decide, nearest deadline first |
| `POST /reviewer/requests/{id}/approve`, `.../deny` | — | The Reviewer's decision |
| `GET /reviewer/denial-reason-codes` | — | The coded Denial Reasons the plan has configured, for the page's picker |

The wire shapes mirror Da Vinci's `br-payer` reference implementation so that its companion `br-provider` can talk
to this service; see [docs/adr/0001](docs/adr/0001-target-current-da-vinci-guide-versions.md) for the guide
versions chosen and why. The vocabulary the code and tests use is in [GLOSSARY.md](GLOSSARY.md).

Every request and inquiry Bundle is checked first by `br-payer`'s structural checks, then against the PAS 2.2.1
profiles (`profile-pas-request-bundle`, `profile-pas-inquiry-request-bundle` and everything they pull in, US Core
included) by the [Firely validator](https://github.com/FirelyTeam/firely-validator-api). A Bundle that breaks a
profile is answered with a 400 `OperationOutcome` carrying the validator's findings, one issue per finding. The
profiles come from the published FHIR package `hl7.fhir.us.davinci-pas` 2.2.1 and the dependencies its manifest
names, fetched from `packages.fhir.org` on first start into `fhir-packages/` next to the project (about 40 MB,
once; the folder is ignored by git and a copy of it is all an offline machine needs). Two things to know:

- X12 owns the value sets several PAS elements bind to (service categories, certification types, the relationship
  code beside the FHIR one on a Coverage), and they are not in any package. Where PAS binds to an X12 value set
  the service cannot hold, a code from the X12 code system of that list is accepted on its system alone and a
  code from any other system is not (`X12ValueSetsByCodeSystem`); that is also what lets the validator tell PAS's
  X12 coding slices from the FHIR codings beside them. Any other code it cannot check is a warning in the log,
  never a rejection.
- Firely 3.6.0 cannot compile the PAS Claim profiles as published (their `careTeam` slicing discriminator ends in
  `ofType(boolean)`; [firely-validator-api#654](https://github.com/FirelyTeam/firely-validator-api/issues/654)).
  `PasProfiles` hands the validator those definitions with the `ofType()` dropped, which names the same element.

## Running it

Prerequisites: the .NET 10 SDK and a SQL Server instance (2019 or later; Developer or Express editions are free)
reachable as `localhost` with Windows authentication — change `ConnectionStrings:PriorAuthorization` in
`appsettings.json` for another instance or login. The database `Cms0057PriorAuthorization` is created and migrated
on first start, and three synthetic Members (`90000001A`, `90000002B`, `90000003C`) are seeded in Development.

```bash
dotnet run --project src/Cms0057.PriorAuthorization
```

The service listens on `http://localhost:5198`. The quickest tour is
[`PriorAuthorization.http`](src/Cms0057.PriorAuthorization/PriorAuthorization.http): open it in Visual Studio or
VS Code (REST Client) and send the requests top to bottom. A sample request Bundle is in
[`samples/`](samples/residential-request.json), and [`samples/br-provider/`](samples/br-provider/replay.sh) replays
the calls Da Vinci's `br-provider` makes to a payer, in its payload shapes and with its token flow, against the
running service. The Reviewer's page is at
[http://localhost:5198/reviewer](http://localhost:5198/reviewer): it lists the Pended requests, soonest Decision
Deadline first and Overdue ones marked, and approves or denies each one through the Reviewer endpoints. It is a
single static page ([`wwwroot/reviewer.html`](src/Cms0057.PriorAuthorization/wwwroot/reviewer.html)) with no
sign-in, as the rest of the proof of concept has none.

The service's own settings are in [`appsettings.json`](src/Cms0057.PriorAuthorization/appsettings.json): the
connection string, the registered client systems (`Authentication`, below), the rules (`Requirements`: which
billing codes need prior authorization, how long the plan has to decide, standard and expedited, and the canonical
of the Documentation Questionnaire that goes with the request), the plan's licensed Denial Reason codes, and the
profile validation (`ValidateProfiles`, on by default; `FhirPackageServer` and `FhirPackageFolder` say where the
packages come from and where they are kept). The residential codes (`H0019` with `U1`/`U2`/`U3`) come from the
DMC-ODS billing manual; the mental health codes under BHIN 26-030 are still to be confirmed against the 2026 manual.

The questionnaires themselves are FHIR Questionnaires, one per file in
[`questionnaires/`](src/Cms0057.PriorAuthorization/questionnaires) (`QuestionnaireFolder` to keep them elsewhere).
The one shipped, *Residential Treatment Documentation*, is a starting point drawn from the DMC-ODS medical necessity
requirements, in which the level of care follows from an ASAM Criteria assessment: the ASAM level and assessment
date, the diagnosis, the admission date and length of stay requested, why a less intensive level of care would not
do, and any residential episode in the last twelve months. **The plan's utilization management staff have yet to
confirm those questions**; it is marked `draft`. Every questionnaire is also offered as an Adaptive Questionnaire
under its canonical with `-adaptive` appended: the package then carries the first questions in the response's
contained Questionnaire, and `$next-question` adds the next set, from the first question not yet asked up to the
next one that later questions depend on (an `enableWhen`), leaving out questions whose condition the answers do
not meet, until nothing is left and the response is `completed`. The BHIN 26-030 services name no questionnaire
yet, so their coverage information says prior authorization is needed and nothing about documentation.

### Running Da Vinci's br-provider against it

[`br-provider`](https://github.com/HL7-DaVinci/br-provider) is Da Vinci's provider-side reference implementation
(an EHR front end over a HAPI FHIR server). Driven end to end against this service on one machine, a request it
submits is Pended here, decided on the Reviewer's page, and the decision shows in br-provider. What it takes:

1. **Tools.** Java 17+, Maven 3.9 and Node 22 (or Bun). br-provider's own security server (`fast-udap-security`)
   and `br-payer` are not needed; both are Docker images and this service stands in for the payer.
2. **br-provider's server**, with its own authentication off (it cannot start it without that security server)
   and, if port 8080 is taken, on another port. Its frontend's dev proxy is pinned to 8080, so serve the built
   frontend from the server instead:

   ```bash
   git clone https://github.com/HL7-DaVinci/br-provider && cd br-provider
   npm install --legacy-peer-deps --ignore-scripts
   cd frontend && npx cpy ../node_modules/lforms/dist/lforms/webcomponent/{styles.css,runtime.js,polyfills.js,main.js,assets/lib/zone.min.js,down_arrow_gray_10_10.png,magnifying_glass.png} ../node_modules/lforms/dist/lforms/fhir/R4/lformsFHIR.min.js public/lforms --flat && node scripts/scope-lforms-css.mjs
   npx vite build && mkdir -p ../server/src/main/resources/static && cp -r dist/* ../server/src/main/resources/static/
   cd ../server && mvn spring-boot:run "-Dspring-boot.run.arguments=--server.port=8090 --hapi.fhir.server_address=http://localhost:8090/fhir --security.enable-authentication=false --security.fhir-base-url=http://localhost:8090/fhir --security.external-base-url=http://localhost:8090 --app.provider-servers[0].name=Local_Provider_Server --app.provider-servers[0].url=http://localhost:8090/fhir"
   ```

   (The `cpy` and `vite build` lines are what its `postinstall` and `build` scripts do; they are run by hand
   because its scripts assume Bun's module layout. Its UI is then at [http://localhost:8090](http://localhost:8090).)
3. **This service**, with the CDS Hooks token requirement off. br-provider authenticates CDS Hooks calls with the
   CDS Hooks client JWT its security server's certificate signs, so without that server it sends no credentials at
   all on CRD calls; its PAS calls use SMART Backend Services and need no such allowance:

   ```bash
   dotnet run --project src/Cms0057.PriorAuthorization -- --Authentication:RequireTokenForCdsHooks=false
   ```

   br-provider is registered in `appsettings.json` as client `br-provider`, with its keys fetched from
   `http://localhost:8090/api/security/jwks` (on Windows it generates new keys on every start, so they are never
   copied in).
4. **In br-provider:** Settings, Payer tab, Server "Custom...": CDS Services URL `http://localhost:5198/cds-services`,
   FHIR URL `http://localhost:5198/fhir`, Authentication "SMART Backend Services", Client ID `br-provider`. Then
   give a patient's Coverage a `subscriberId` that is a Member of this plan, for example Bobby Tables' (`cov1234`):

   ```bash
   curl -s http://localhost:8090/fhir/Coverage/cov1234 | sed 's/"subscriberId" *: *"[^"]*"/"subscriberId": "90000001A"/' | curl -s -X PUT -H 'Content-Type: application/fhir+json' --data-binary @- http://localhost:8090/fhir/Coverage/cov1234
   ```

   br-provider derives the type `MB` member identifier it sends from `Coverage.subscriberId`. Its database is
   in memory, so this is repeated after each restart.
5. **The flow:** open the patient, Start Encounter, "Custom order code..." with Order Type Service, Code System
   HCPCS and code `H2012` (a code this plan pends; br-provider sends no modifiers, so `H0019` comes back Not
   Required), pick the coverage, Sign All Orders. CRD answers `auth-needed`, and the order gets a "Submit PA"
   action. br-provider stamps the signed-in practitioner on the request, and with its authentication off there is
   none; set one in the browser console first (`sessionStorage.setItem('spa_userinfo', JSON.stringify({fhirUser:
   'Practitioner/pra1234', fhirUserType: 'Practitioner'}))`, its seeded Dr. Doe, NPI `1122334455`). Submit: the
   response is Pended and br-provider has subscribed. Approve it on
   [http://localhost:5198/reviewer](http://localhost:5198/reviewer); the notification lands at
   `http://localhost:8090/api/pas/notification` and br-provider's page shows Approved with the Authorization Number.

## Prior Authorization Information for the Patient Access API

From January 1, 2027 the Patient Access API has to show a Member their prior authorization requests and decisions
(42 CFR 422.119(b)(1)(iv), applied to Medicaid managed care by 438.242(b)(5)): the status, the date approved or
denied, the date or circumstance under which the authorization ends, the items and services approved, the specific
reason for a denial, and related documentation the provider submitted; accessible within one business day of the
request and of every status change, and kept for as long as the authorization is active and at least a year after
its last status change. Drugs are excluded, and this plan prior-authorizes none.

This project assumes the agency runs its Patient Access API already (a vendor's, from the 2020 rule; unconfirmed,
see the issues), so what is built here is the payer side it reads from, not a Member-facing app: the Member's own
SMART App Launch, identity proofing and consent stay with that API. It authenticates here like any other system
(SMART Backend Services, scope `system/ExplanationOfBenefit.read`) and reads:

- `GET /fhir/ExplanationOfBenefit?patient={member id}`: every request ever made for the Member, whoever sent it,
  oldest first, each a [PDex 2.1 Prior Authorization](https://hl7.org/fhir/us/davinci-pdex/STU2.1/StructureDefinition-pdex-priorauthorization.html)
  `ExplanationOfBenefit` (the guide the rule recommends). `patient` is required, as PDex's server capability has
  it. `_lastUpdated=ge2026-11-06T19:00:00Z` narrows it to the requests whose status changed since, which is how
  the Patient Access API keeps within the one-business-day window without reading everything again.
- `GET /fhir/ExplanationOfBenefit/{id}`: one of them.

Each `ExplanationOfBenefit` is the request's PAS ClaimResponse told again in PDex terms, so a Member and their
provider read the same decision: `use` `preauthorization`; `created` the date received; `meta.lastUpdated` the
last status change; `outcome` `queued` while Pended and `complete` once answered; `status` `cancelled` when the
provider withdrew it. The review action (X12 306: `A1` approved, `A2` denied or cancelled, `A3` not required, `A4`
Pended) is on the `adjudication` of the request and of each item, with the date decided (`when-adjudicated`), the
Authorization Number, and on a denial the Denial Reason as the review action's `reasonCode`: the plan's X12 886
code when one was given and the Reviewer's words as its text, repeated as a process note. An approval carries the
authorization period (`preAuthRefPeriod`, and PAS's `itemPreAuthPeriod` on each item) from the day of the decision
to the end date the Reviewer gave. Items repeat the services asked for (HCPCS code and modifiers, category,
place of service, quantity) with their trace numbers. Nothing is ever deleted, so the retention requirement is met
by keeping everything; a retention schedule beyond that is the agency's.

PDex wants a Money on the adjudication that carries the review action; a prior authorization states no amount
unless the provider priced what it asked for (`Claim.item.net`), so otherwise the Money names the currency and no
value rather than a zero that would read as a statement. Checked against the published profile with the Firely
validator, an `ExplanationOfBenefit` from here has no errors; what remains are terminology warnings for the X12 and
HCPCS code systems no FHIR package carries, and the profile's own `adjudication-has-amount-type-slice` invariant,
which names a value set URL the guide misspells (`davinc-pdex`) and so cannot be evaluated by any validator.

Not in it: the related documentation submitted by the provider (none is received yet; see the issues on DTR), the
units used to date (a claims figure PDex puts in `total`, which this service has no claims to compute from), and
the `Patient`, `Coverage` and `Organization` resources the references point at (they are the request's own, with
the organizations' names as display text).

## Provider Access for in-network providers

From January 1, 2027 an in-network provider may retrieve, through the Provider Access API, the information of the
Members attributed to it (claims, encounters, clinical data and the same prior authorization information as above,
with the provider's related documentation), within one business day of asking, unless the Member has opted out
(42 CFR 422.121(a); Medicaid managed care through 438.242(b)(7)). The rule has the plan run an attribution process,
an opt-out process available at all times with plain-language information for Members, and provider resources
explaining how to use it. Da Vinci PDex 2.1 shapes the exchange as a `Group` per provider (its Attribution List)
and a Bulk Data export of the Group.

What is built, for the prior authorization information this service holds:

- **Attribution.** A provider is attributed a Member when it sends a Prior Authorization Request for them (the
  treatment relationship the Member's own care shows), recorded under the NPI the request's provider carries; and
  the plan's staff can attribute a Member to a provider themselves (`POST /members/{member}/attributions` with the
  NPI), for an assigned provider, say. The Da Vinci guide recommends attribution by rendering-provider NPI, which
  is what this is; a payer with a provider roster would attribute from it as well.
- **Opt-Out.** `PUT /members/{member}/provider-access` with `{"optedOut": true}` (or `false` to opt back in) records
  the Member's choice; opting out keeps the Member off every provider's list and refuses every provider read, and
  opting back in restores the Attributions. The Member-facing form and the educational material the rule requires
  are the agency's, outside this service.
- **The Attribution List.** A provider system is registered with its organization's NPI (`OrganizationIdentifier`
  under `Authentication:Clients`). `GET /fhir/Group` shows it its own list, and nobody else's, as a
  [PDex Provider Group](https://hl7.org/fhir/us/davinci-pdex/STU2.1/StructureDefinition-pdex-provider-group.html)
  (id `provider-{NPI}`, status `open`, one `member` per Member who has not opted out); `GET /fhir/Group/{id}` reads
  it. Another provider's list answers 404, as if it were not there.
- **Export.** `POST` (or `GET`) `/fhir/Group/{id}/$export` with `Prefer: respond-async` starts a
  [Bulk Data 1.0](https://hl7.org/fhir/uv/bulkdata/STU1/) export: 202 with `Content-Location` pointing at the status
  URL, which answers the manifest (`transactionTime`, `request`, `requiresAccessToken: true`, one `output` file per
  resource type with its count); the file is newline-delimited JSON (`application/fhir+ndjson`) of the Members' PDex
  Prior Authorization `ExplanationOfBenefit`s, the same as the Patient Access API gets. `_since` narrows it to what
  changed since, `_type` may name `ExplanationOfBenefit` (anything else is 400, since nothing else is exported),
  `_outputFormat` must be ndjson. The data is small, so the export is complete the moment it starts; jobs live in
  memory for the life of the process, and only the system that started one can read or `DELETE` it. The Member's
  opt-out is applied when the export runs, as PDex asks.
- **One Member.** A provider system may also read `GET /fhir/ExplanationOfBenefit?patient={member}` for a Member
  attributed to it; for anyone else it is 403. The Patient Access API's reads are unaffected by opt-outs, which
  concern providers only.

Scopes: `system/Group.read` for the list, `system/ExplanationOfBenefit.read` for the export and the single-Member
read. Not built: PDex's `Consent` resource for the opt-out (the choice is recorded in the plan's own records), its
draft `$davinci-data-export` operation (the rule names Bulk Data `$export`), and the claims, encounter and clinical
data the rule also puts in this API, which this service does not hold.

## Payer-to-Payer exchange with another plan

From January 1, 2027 a Member who moves to, or also holds coverage with, another Impacted Payer may have their
information follow them, if they opt in (42 CFR 422.121(b); Medicaid managed care through 438.242(b)(7)). The
plan shares claims, encounter and clinical data and the prior authorization information above, for dates of
service within five years, leaving out provider remittances, cost-sharing and denied prior authorizations, within
one week of the other payer's request; the other payer attests the Member's enrollment and opt-in. Da Vinci PDex 2.1
shapes it as HRex `$member-match` to identify the Member, a `Group` of matched Members per payer, and Bulk Data or
`$everything` to fetch.

What is built, for the prior authorization information this service holds:

- **Opt-In.** `PUT /members/{member}/payer-to-payer` with `{"optedIn": true}` (or `false` to withdraw) records the
  Member's choice; nothing is shared with another payer until they opt in, and withdrawing stops the sharing at
  once. The Member-facing form is the agency's, outside this service.
- **Member match.** Another payer is registered with `Kind: Payer` and its organization identifier. It sends
  `POST /fhir/Patient/$member-match`
  ([HRex](https://hl7.org/fhir/us/davinci-hrex/STU1.1/OperationDefinition-member-match.html) `Parameters`:
  `MemberPatient`, the person as it knows them; `CoverageToMatch`, the coverage it believes this plan gave them;
  `Consent`, the Member's permission, shaped to HRex's Consent profile). The plan looks for one Member whose
  identifier the Coverage or Patient carries and whose name agrees; it answers the `MemberIdentifier` (type `UMB`)
  and `MemberId` (`Patient/{member}`) when exactly one fits, the Member has opted in, and the Consent permits
  disclosure for a period that has not ended. Anything else is 422 with an OperationOutcome saying which, as HRex
  has it; a malformed request is 400. A match is remembered, with the Consent's end date.
- **The matched Members.** `GET /fhir/Group` shows the payer its own
  [PDex Member Match Group](https://hl7.org/fhir/us/davinci-pdex/STU2.1/StructureDefinition-pdex-member-match-group.html)
  (id `payer-{identifier}`, code `match`, one `member` per matched Member who is still opted in and whose Consent
  is still in its period), and `GET /fhir/Group/{id}` reads it; nobody else's Group is there.
- **Fetching.** `Group/{id}/$export` works as for Provider Access, `GET /fhir/ExplanationOfBenefit?patient=`
  for one matched Member, and `GET /fhir/Patient/{member}/$everything` answers the Member as a `Patient` followed
  by their prior authorization information. In every case what another payer gets is filtered as the rule has it:
  **denied requests are left out**, as are requests from more than five years ago and requests that are no longer
  active (Pended, or Approved for a period not yet ended) whose status last changed more than a year ago, which is
  the PDex payer-to-payer scope. A request that is not shared answers 403 when read by id.
- **Conformance.** The Group validates against PDex 2.1.0; the match request and answer against HRex 1.1.0's
  Parameters profiles (checked with the Firely validator; the checks are not kept as tests because they need the
  PDex, ATR and HRex packages, which the service does not ship).

Scopes: `system/Patient.read` for `$member-match` and `$everything`, `system/Group.read` for the Group,
`system/ExplanationOfBenefit.read` for the export and the single-Member read. The sample client
`sample-other-payer` signs with the same synthetic key as the others. Not built: PDex's bulk member match
(`$bulk-member-match`), the plan acting as the *requesting* payer (asking a Member's old plan for their
information), unstructured documentation (no QuestionnaireResponse is received yet, see above), and the claims,
encounter and clinical data this service does not hold.

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

Scopes: `$submit` needs `system/Claim.write`, `$inquire` `system/Claim.read`, reading a ClaimResponse
`system/ClaimResponse.read`, a Decision Subscription `system/Subscription.write`, looking one up
`system/Subscription.read`, the two DTR operations `system/Questionnaire.read` (`br-provider` asks for
`system/*.rs`, which is granted as that), the Patient Access API's and a provider's prior authorization reads
`system/ExplanationOfBenefit.read` (the sample client `sample-patient-access-api` signs with the same synthetic key),
a provider's Attribution List or a payer's matched Members `system/Group.read`, and another payer's `$member-match`
and `$everything` `system/Patient.read`; CRD needs only a token from a registered client. What a client may see
follows from its registration, not its scopes: `Kind` is `ProviderSystem` (the default; with
`OrganizationIdentifier`, its NPI), `PatientAccessApi` or `Payer` (with `OrganizationIdentifier`, what its matches
are kept under). A system may ask in the SMART App Launch 2 spelling instead (`system/Claim.crus`, as `br-provider` does)
and is answered in it; and it is granted the part of what it asked for that it is registered for, the token
response's `scope` saying which, rather than refused outright (`invalid_scope` only when none of it can be granted).
The metrics report under `/reports`
is public, as the rule intends. The Reviewer endpoints under `/reviewer` are for the plan's own staff and are not
covered by SMART Backend Services; their protection is a separate concern, tracked in the issues. Access tokens are
signed with a key generated when the service starts, so they do not survive a restart.

[`PriorAuthorization.http`](src/Cms0057.PriorAuthorization/PriorAuthorization.http) walks through it: step A
trades a pasted assertion for a token and the later steps carry that token.

## Tests

```bash
dotnet test
```

The tests drive the service over HTTP only, each with a throwaway database on the same SQL Server (set
`CMS0057_TEST_SQLSERVER` to use another instance) and a fake clock. They are
written as the behaviour a provider system or a Reviewer would see, and read best as a specification:
[tests/](tests/Cms0057.PriorAuthorization.Tests).

## What is and is not built

Built: PAS submit, inquire, ClaimResponse read, subscription notifications and subscription look-up; CRD
order-sign with the documentation needed; DTR questionnaire packages and adaptive forms; configurable rules with
standard and expedited deadlines (24 hours for residential treatment; 5 business days / 72 hours for the BHIN
26-030 mental health services); several items per request; updates, cancellations and renewals; approval and
denial with the rule's required content, including coded Denial Reasons; overdue tracking; the Reviewer's page; the
public metrics report; SMART Backend Services authentication for the provider-facing endpoints; validation of
requests and inquiries against the PAS profiles; Prior Authorization Information for the Patient Access API as PDex
Prior Authorization ExplanationOfBenefit resources; Provider Access with Attribution, Opt-Out, Attribution Lists
and Bulk Data export; Payer-to-Payer with Opt-In, HRex member match, the matched Members' Group, `$everything` and
the rule's filtering (no denials, five years, a year after the last status change unless still active).

Not built, tracked as [issues](https://github.com/tngo0508/CMS-0057-F/issues): DTR prepopulation beyond the order
itself (no CQL libraries ship; the plan fills in only the service requested), receiving a completed
QuestionnaireResponse with a request (and so the related documentation in the Patient Access, Provider Access and
Payer-to-Payer information), the Reviewer and staff endpoints' own authentication, the Member-facing side of the
Patient Access API, the Opt-Out and the Opt-In (assumed to be the agency's), the plan as the requesting payer in a
Payer-to-Payer exchange, and the claims, encounter and clinical data the three access APIs also carry, which this
service does not hold. Several issues are questions for the agency rather than work items.

## Sources and attribution

- CMS-0057-F final rule and fact sheet; DHCS BHIN 24-001, 26-008 and 26-030.
- HL7 Da Vinci PAS 2.2.1, CRD 2.2.1, DTR 2.2.0; FHIR R4.0.1; Subscriptions Backport; SMART App Launch 1.0.0
  Backend Services.
- The request and inquiry Bundles used in the tests and samples are adapted from the examples in Da Vinci's
  [`br-payer`](https://github.com/HL7-DaVinci/br-payer) reference implementation (Apache License 2.0), cut down and
  re-coded for behavioral health services. The FHIR model and serialisation come from the
  [Firely .NET SDK](https://github.com/FirelyTeam/firely-net-sdk), the profile validation from the
  [Firely validator](https://github.com/FirelyTeam/firely-validator-api) and
  [Firely.Fhir.Packages](https://github.com/FirelyTeam/Firely.Fhir.Packages) (all BSD-3-Clause).
- The FHIR packages the service fetches (Da Vinci PAS, US Core, HRex, the Subscriptions Backport, FHIR R4 and
  the rest) are HL7's, published under CC0.

## Licence

[MIT](LICENSE). The request examples adapted from `br-payer` remain under their Apache License 2.0, as noted
above.
