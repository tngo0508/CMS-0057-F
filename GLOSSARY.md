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
is received and set by the rule for the service requested.
_Avoid_: Due date, turnaround time, SLA

**Overdue**:
The state of a Prior Authorization Request whose Decision Deadline has passed without a Prior Authorization Decision.
_Avoid_: Late, expired, deemed denied

**Not Required**:
The immediate answer to a Prior Authorization Request for a service the Behavioral Health Plan does not prior-authorize.
It is not a Prior Authorization Decision and the request never becomes Pended.
_Avoid_: Auto-approved, exempt, no auth needed

**Authorization Number**:
The identifier the Behavioral Health Plan issues with an approval, which the provider quotes when delivering and
billing the service.
_Avoid_: Auth ID, reference number, tracking number

**Denial Reason**:
The specific, plain-language explanation a Reviewer must give when denying a Prior Authorization Request.
_Avoid_: Rejection reason, denial code

### The four APIs

**Patient Access API**:
The interface through which a person retrieves their own claims, encounter, clinical and prior authorization
information from the Behavioral Health Plan.

**Provider Access API**:
The interface through which an in-network provider with a treatment relationship retrieves a person's claims,
encounter, clinical and prior authorization information, unless that person has opted out.

**Payer-to-Payer API**:
The interface through which Impacted Payers exchange a person's information when coverage changes or is concurrent,
only after that person has opted in.

**Prior Authorization API**:
The interface through which a provider learns whether prior authorization is required, what documentation is needed,
and submits a request and receives the decision.
_Avoid_: PARDD API (the proposed rule's name)

### Sources

**Reference Implementation**:
One of the HL7 Da Vinci project's published sample systems whose observable behaviour this project reproduces.
_Avoid_: Reference architecture, sample code
