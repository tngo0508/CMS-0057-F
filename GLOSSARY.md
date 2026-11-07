# CMS-0057-F Interoperability and Prior Authorization

How the Orange County Health Care Agency, as a payer, meets the data exchange and prior authorization obligations of
the CMS Interoperability and Prior Authorization Final Rule.

## Language

### The rule

**Final Rule**:
The CMS Interoperability and Prior Authorization Final Rule, CMS-0057-F, published January 17, 2024. It is the only
authoritative statement of the obligations in this project.
_Avoid_: Proposed rule, CMS-0057-P, the fact sheet

**Impacted Payer**:
An organization the Final Rule obligates: Medicare Advantage organizations, state Medicaid and CHIP fee-for-service
programs, Medicaid managed care plans, CHIP managed care entities, and Qualified Health Plan issuers on the Federally
Facilitated Exchanges.
_Avoid_: Insurer, carrier, health plan (when the regulatory category is meant)

**Behavioral Health Plan**:
The Orange County Health Care Agency acting as the county Mental Health Plan and Drug Medi-Cal Organized Delivery
System. It is the Impacted Payer in this project.
_Avoid_: OCHCA, the County, the Agency, CalOptima (a separate Impacted Payer)

### Prior authorization

**Member**:
A person covered by the Behavioral Health Plan.
_Avoid_: Patient (except as the name of the FHIR resource), client, beneficiary, enrollee

**Prior Authorization Request**:
A provider's request that the Behavioral Health Plan approve a service for a Member before it is delivered.
_Avoid_: TAR, SAR, auth, authorization (unqualified)

**Requested Service**:
One service a Prior Authorization Request asks for. A request may ask for several; each is answered on its own, but
the request is Pended as a whole when any of them needs review, with the tightest Decision Deadline among them, and
the Reviewer decides it as a whole.
_Avoid_: Line, item (except as the name of the FHIR element), procedure

**Prior Authorization Decision**:
The Behavioral Health Plan's final answer to a Prior Authorization Request: approved or denied. A Pended request has no
decision yet.
_Avoid_: Determination, adjudication, response

**Pended**:
The state of a Prior Authorization Request that has been received and awaits a Reviewer's decision. The Decision
Deadline keeps running while a request is Pended.
_Avoid_: Pending decision, in review, on hold

**Reviewer**:
A clinician at the Behavioral Health Plan who makes the Prior Authorization Decision on a Pended request.
_Avoid_: Approver, adjudicator, utilization manager

**Decision Deadline**:
The moment by which the Behavioral Health Plan must have made its Prior Authorization Decision, fixed when the request
is received and set by the rule for the service requested. Where several rules apply, the earliest deadline stands.
_Avoid_: Due date, turnaround time, SLA

**Expedited**:
A Prior Authorization Request the provider has asked to have decided on the shorter time frame because the standard one
could seriously jeopardize the Member's life, health or ability to regain maximum function. The provider's say-so at
submission; the plan does not second-guess it here.
_Avoid_: Urgent, stat (except as the FHIR priority code), rush

**Overdue**:
The state of a Prior Authorization Request whose Decision Deadline has passed without a Prior Authorization Decision.
_Avoid_: Late, expired, deemed denied

**Cancelled**:
The state of a Prior Authorization Request the provider has withdrawn. A Pended request leaves the Reviewer's list;
an approved one loses its authorization; a denied one cannot be cancelled. A cancellation is never a Prior
Authorization Decision and never counts as a denial.
_Avoid_: Voided, withdrawn (as a state name), revoked

**Not Required**:
The immediate answer to a Prior Authorization Request for a service the Behavioral Health Plan does not prior-authorize.
It is not a Prior Authorization Decision and the request never becomes Pended.
_Avoid_: Auto-approved, exempt, no auth needed

**Authorization Number**:
The identifier the Behavioral Health Plan issues with an approval, which the provider quotes when delivering and
billing the service.
_Avoid_: Auth ID, reference number, tracking number

**Denial Reason**:
The specific, plain-language explanation a Reviewer must give when denying a Prior Authorization Request. A Denial
Reason Code may go with it, never in place of it.
_Avoid_: Rejection reason

**Denial Reason Code**:
One of the coded reasons for a denial that the Behavioral Health Plan has chosen to use, taken from the X12 Review
Decision Reason Codes that the payer-to-provider standards carry. Optional, and always accompanied by the Denial Reason
in words.
_Avoid_: Denial code, reject reason code

**Decision Subscription**:
A provider system's standing request to be told, at an address it names, whenever a Prior Authorization Request its
organization sent receives a Prior Authorization Decision.
_Avoid_: Webhook, callback, notification subscription (unqualified)

**Coverage Information**:
The Behavioral Health Plan's answer, attached to an order as the clinician signs it, saying whether the service is
covered, whether a Prior Authorization Request is needed and, if so, which Documentation Questionnaire goes with it.
_Avoid_: Card, coverage determination, eligibility response

**Documentation Questionnaire**:
The questions the Behavioral Health Plan asks a provider to answer and send with a Prior Authorization Request for a
given service, so that a Reviewer can decide it. Which one applies follows from the service requested; the plan
hands it out already started, with what it can fill in itself.
_Avoid_: Form, template, DTR questionnaire, attachment

**Adaptive Questionnaire**:
A Documentation Questionnaire the Behavioral Health Plan hands out a few questions at a time, choosing each next set
from the answers so far, until it says the questionnaire is complete.
_Avoid_: Wizard, dynamic form, interview

**Prior Authorization Information**:
What the Behavioral Health Plan shows a Member about each of their Prior Authorization Requests through the Patient
Access API: its status, the date it was approved or denied, when the authorization ends, the Requested Services and
which were approved, and the Denial Reason.
_Avoid_: PA data, EOB, prior auth record, prior authorization history

**Attribution**:
The Behavioral Health Plan's record that a Member is a patient of an in-network provider, made when the provider
sends a Prior Authorization Request for the Member or when the plan's staff record it. What lets the provider see
the Member's information through the Provider Access API.
_Avoid_: Roster, panel, assignment, treatment relationship (unqualified)

**Attribution List**:
The Members attributed to one provider organization who have not opted out, as that provider sees it and exports it.
_Avoid_: Group (unqualified), patient list, member list

**Opt-Out**:
A Member's choice that their information not be shared with their providers through the Provider Access API.
Information is shared unless the Member opts out, and the choice can be changed at any time.
_Avoid_: Consent withdrawal, block, restriction, opt-in (that is the Payer-to-Payer choice)

### The four APIs

**Patient Access API**:
The interface through which a person retrieves their own claims, encounter, clinical and Prior Authorization
Information from the Behavioral Health Plan.

**Provider Access API**:
The interface through which an in-network provider retrieves the claims, encounter, clinical and Prior Authorization
Information of the Members attributed to it (see Attribution), unless the Member has opted out (see Opt-Out).

**Payer-to-Payer API**:
The interface through which Impacted Payers exchange a person's information when coverage changes or is concurrent,
only after that person has opted in.

**Prior Authorization API**:
The interface through which a provider learns whether prior authorization is required (Coverage Information), what
documentation is needed (the Documentation Questionnaire), and submits a request and receives the decision.
_Avoid_: PARDD API (the proposed rule's name)

### Sources

**Reference Implementation**:
One of the HL7 Da Vinci project's published sample systems whose observable behaviour this project reproduces.
_Avoid_: Reference architecture, sample code
