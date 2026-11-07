# Notes

Detail that left the README. Same facts, longer form.

## Request validation

Every `$submit` / `$inquire` Bundle: first `br-payer`'s structural checks, then the PAS 2.2.1 profiles
(`profile-pas-request-bundle`, `profile-pas-inquiry-request-bundle`, everything they pull in incl. US Core) via the
[Firely validator](https://github.com/FirelyTeam/firely-validator-api). Profile break → 400 `OperationOutcome`,
one issue per finding.

Packages: `hl7.fhir.us.davinci-pas` 2.2.1 + manifest dependencies, fetched from `packages.fhir.org` on first start
into `fhir-packages/` (~40 MB, once, git-ignored; copy the folder for an offline machine).

Two caveats:

- **X12 value sets** (service categories, certification types, X12 relationship code) are not in any package.
  Where PAS binds to one, a code from the matching X12 code system is accepted on system alone; any other system is
  rejected (`X12ValueSetsByCodeSystem`). Also how the validator tells X12 slices from the FHIR codings beside them.
  Other uncheckable codes → log warning, never a rejection.
- **Firely 3.6.0 cannot compile the PAS Claim profiles** as published (`careTeam` slicing discriminator ends in
  `ofType(boolean)`; [firely-validator-api#654](https://github.com/FirelyTeam/firely-validator-api/issues/654)).
  `PasProfiles` hands the validator the definitions with `ofType()` dropped; same element.

## Questionnaires

FHIR Questionnaires, one per file in `questionnaires/` (`QuestionnaireFolder` to relocate). Shipped:
*Residential Treatment Documentation*, drawn from DMC-ODS medical necessity rules: ASAM level + assessment date,
diagnosis, admission date, length of stay, why a lower level of care won't do, residential episodes in the last
12 months. Marked `draft`: UM staff have not confirmed it.

Adaptive form: every questionnaire also served under `{canonical}-adaptive`. Package carries the first questions
in the response's contained Questionnaire; `$next-question` adds the next set, from the first unasked question up
to the next one later questions depend on (`enableWhen`), skipping questions whose condition the answers fail,
until `completed`.

BHIN 26-030 services name no questionnaire yet → coverage information says auth needed, nothing about documentation.

## PDex ExplanationOfBenefit mapping

Each EOB = the request's PAS ClaimResponse retold in PDex terms, so Member and provider read the same decision.

| EOB element | Value |
| --- | --- |
| `use` | `preauthorization` |
| `created` | date received |
| `meta.lastUpdated` | last status change |
| `outcome` | `queued` while Pended, `complete` once answered |
| `status` | `cancelled` when provider withdrew |
| `adjudication` (request + each item) | review action X12 306: `A1` approved, `A2` denied/cancelled, `A3` not required, `A4` Pended; `when-adjudicated`; Authorization Number |
| denial | review action `reasonCode`: X12 886 code if given, Reviewer's words as text; repeated as process note |
| approval | `preAuthRefPeriod` + PAS `itemPreAuthPeriod`: decision day → Reviewer's end date |
| items | HCPCS code + modifiers, category, place of service, quantity, trace numbers |

Money: PDex wants a Money on the adjudication carrying the review action. A prior auth states no amount unless the
provider priced it (`Claim.item.net`); otherwise currency only, no value (a zero would read as a statement).

Validation: 0 errors vs the published profile. Remaining: terminology warnings for X12/HCPCS (no package carries
them) and the profile's `adjudication-has-amount-type-slice` invariant, whose value set URL the guide misspells
(`davinc-pdex`) so no validator can evaluate it.

Not in the EOB: provider documentation (none received yet), units used to date (claims figure, no claims here),
referenced `Patient` / `Coverage` / `Organization` (request's own, organization names as display).

## Provider Access details

- Attribution by rendering-provider NPI = what the Da Vinci guide recommends. A payer with a roster would add that.
- Opt-out applied when the export runs, as PDex asks. Patient Access reads unaffected by opt-outs.
- Export manifest: `transactionTime`, `request`, `requiresAccessToken: true`, one `output` per resource type with
  count. `_type` other than `ExplanationOfBenefit` → 400. `_outputFormat` must be ndjson. Only the starting system
  can read or `DELETE` a job.

## Payer-to-Payer details

- Consent shaped to HRex's Consent profile; its provision period end = how long the match lasts. Consent already
  ended at match time → 422. Match remembered with that end date; re-match renews it.
- Withdrawing opt-in keeps the matches; opting back in restores them.
- Filter = PDex payer-to-payer scope: not Denied; `ReceivedAt` within 5 years; active (Pended, or Approved with
  end date unset or ≥ today) or last status change within 1 year.
- Conformance checks (Group vs PDex 2.1.0, Parameters vs HRex 1.1.0) were run with the Firely validator but are not
  kept as tests: they need the PDex, ATR and HRex packages, which the service does not ship.

## Authentication details

- Access tokens signed with a key generated at start → don't survive restart.
- Partial scope grant: client gets the subset it is registered for; `invalid_scope` only when none can be granted.
- `br-provider` asks `system/*.rs` → granted as the read scopes it is registered for.
- Metrics under `/reports` public by design. `/reviewer/*` and `/members/*` unprotected: staff auth is an open issue.

## Running Da Vinci's br-provider against it

[`br-provider`](https://github.com/HL7-DaVinci/br-provider) = Da Vinci's provider-side RI (EHR front end over HAPI).
End to end on one machine: it submits, request Pends here, Reviewer decides, decision shows in br-provider.

1. **Tools**: Java 17+, Maven 3.9, Node 22 (or Bun). Not needed: `fast-udap-security`, `br-payer` (Docker; this
   service is the payer).
2. **br-provider server**, auth off (can't start it without its security server), port 8090 if 8080 is taken.
   Frontend dev proxy is pinned to 8080 → build frontend, serve from the server:

   ```bash
   git clone https://github.com/HL7-DaVinci/br-provider && cd br-provider
   npm install --legacy-peer-deps --ignore-scripts
   cd frontend && npx cpy ../node_modules/lforms/dist/lforms/webcomponent/{styles.css,runtime.js,polyfills.js,main.js,assets/lib/zone.min.js,down_arrow_gray_10_10.png,magnifying_glass.png} ../node_modules/lforms/dist/lforms/fhir/R4/lformsFHIR.min.js public/lforms --flat && node scripts/scope-lforms-css.mjs
   npx vite build && mkdir -p ../server/src/main/resources/static && cp -r dist/* ../server/src/main/resources/static/
   cd ../server && mvn spring-boot:run "-Dspring-boot.run.arguments=--server.port=8090 --hapi.fhir.server_address=http://localhost:8090/fhir --security.enable-authentication=false --security.fhir-base-url=http://localhost:8090/fhir --security.external-base-url=http://localhost:8090 --app.provider-servers[0].name=Local_Provider_Server --app.provider-servers[0].url=http://localhost:8090/fhir"
   ```

   (`cpy` + `vite build` = its `postinstall` and `build` scripts, run by hand because they assume Bun's module
   layout.) UI at [http://localhost:8090](http://localhost:8090).
3. **This service**, CDS Hooks token requirement off (br-provider sends no credentials on CRD calls without its
   security server; its PAS calls use SMART Backend Services normally):

   ```bash
   dotnet run --project src/Cms0057.PriorAuthorization -- --Authentication:RequireTokenForCdsHooks=false
   ```

   Registered as client `br-provider`, keys fetched from `http://localhost:8090/api/security/jwks` (new keys
   every start on Windows, so never copied in).
4. **In br-provider**: Settings → Payer → Server "Custom...": CDS Services URL `http://localhost:5198/cds-services`,
   FHIR URL `http://localhost:5198/fhir`, Authentication "SMART Backend Services", Client ID `br-provider`. Give a
   patient's Coverage a `subscriberId` that is a Member here, e.g. Bobby Tables (`cov1234`):

   ```bash
   curl -s http://localhost:8090/fhir/Coverage/cov1234 | sed 's/"subscriberId" *: *"[^"]*"/"subscriberId": "90000001A"/' | curl -s -X PUT -H 'Content-Type: application/fhir+json' --data-binary @- http://localhost:8090/fhir/Coverage/cov1234
   ```

   br-provider derives the `MB` member identifier from `Coverage.subscriberId`. Its DB is in memory → repeat after
   each restart.
5. **Flow**: open patient → Start Encounter → "Custom order code..." Order Type Service, Code System HCPCS, code
   `H2012` (pends; `H0019` without modifiers → Not Required) → pick coverage → Sign All Orders. CRD answers
   `auth-needed`, order gets "Submit PA". br-provider stamps the signed-in practitioner; with auth off set one in the
   browser console first:
   `sessionStorage.setItem('spa_userinfo', JSON.stringify({fhirUser: 'Practitioner/pra1234', fhirUserType: 'Practitioner'}))`
   (its seeded Dr. Doe, NPI `1122334455`). Submit → Pended, br-provider subscribed. Approve at
   [http://localhost:5198/reviewer](http://localhost:5198/reviewer) → notification lands at
   `http://localhost:8090/api/pas/notification`, br-provider shows Approved + Authorization Number.
