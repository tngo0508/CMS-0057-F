#!/usr/bin/env bash
# Replays, against a running service, the calls Da Vinci's br-provider makes to a payer, in its order and with its
# payload shapes (request.json, inquiry.json and subscription.json are what its pas-bundle-builder.ts and
# pas-subscription-builder.ts produce, re-coded for a synthetic Member), authenticating the way its smart-backend
# payer mode does: SMART discovery, an RS384 private_key_jwt assertion, and a token per operation with the scope
# lists from its ProxyUtil.java. Needs bash, curl, python 3 and Windows PowerShell (for New-ClientAssertion.ps1).
#
# Usage: samples/br-provider/replay.sh [root]   (default http://localhost:5198)
# Expect: 200/201 throughout; the H2012 request pended (A4), the H0019 one not required (A3) because br-provider
# sends no modifiers; the inquiry answering with one responseBundle per request; the ClaimResponse readable by id.
set -u
ROOT="${1:-http://localhost:5198}"
BASE="$ROOT/fhir"
DIR="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mktemp -d)"
CT='Content-Type: application/fhir+json'
TOKEN=""
echo "responses are written to $OUT"

summarise() {
  python -I -c '
import json,sys
p=sys.argv[1]
try: d=json.load(open(p,encoding="utf-8"))
except Exception: print("  (not json)", open(p,encoding="utf-8",errors="replace").read()[:300]); sys.exit()
rt=d.get("resourceType")
if rt=="OperationOutcome":
    for i in d.get("issue",[]): print("  issue:", i.get("severity"), i.get("code"), (i.get("diagnostics") or i.get("details",{}).get("text") or "")[:300], i.get("expression"))
elif rt=="Bundle":
    print("  Bundle type", d.get("type"), "total", d.get("total"), "entries", [e.get("resource",{}).get("resourceType") for e in d.get("entry",[])])
    for e in d.get("entry",[]):
        r=e.get("resource",{})
        if r.get("resourceType")=="ClaimResponse":
            print("  ClaimResponse id", r.get("id"), "outcome", r.get("outcome"), "identifiers", [i.get("value") for i in r.get("identifier",[])])
            for it in r.get("item",[]):
                for adj in it.get("adjudication",[]):
                    for ext in adj.get("extension",[]):
                        if ext.get("url","").endswith("extension-reviewAction"):
                            for s in ext.get("extension",[]):
                                if s.get("url","").endswith("extension-reviewActionCode"):
                                    print("   item", it.get("itemSequence"), "reviewActionCode", [c.get("code") for c in s.get("valueCodeableConcept",{}).get("coding",[])])
            for n in r.get("processNote",[]): print("   note:", n.get("text"))
        if r.get("resourceType")=="Subscription":
            print("  Subscription id", r.get("id"), "status", r.get("status"), "endpoint", r.get("channel",{}).get("endpoint"))
elif rt=="Parameters":
    print("  Parameters:", [p.get("name") for p in d.get("parameter",[])])
elif rt=="Subscription":
    print("  Subscription id", d.get("id"), "status", d.get("status"))
elif rt=="ClaimResponse":
    print("  ClaimResponse id", d.get("id"), "outcome", d.get("outcome"), "identifiers", [i.get("value") for i in d.get("identifier",[])])
else:
    print("  ", rt or "", json.dumps(d)[:300])
' "$1"
}

call() { # name method url [file]
  local name="$1" method="$2" url="$3" file="${4:-}"
  local out="$OUT/$name.json"
  local code
  if [ -n "$file" ]; then
    code=$(curl -s -o "$out" -w '%{http_code}' -X "$method" -H "$CT" -H "Authorization: Bearer $TOKEN" --data-binary @"$file" "$url")
  else
    code=$(curl -s -o "$out" -w '%{http_code}' -X "$method" -H "Authorization: Bearer $TOKEN" "$url")
  fi
  echo "== $name: $method $url -> $code"
  summarise "$out"
}

token() { # scopes  (br-provider: discovery, RS384 assertion with aud = token_endpoint, client_credentials)
  local scope="$1"
  local endpoint
  endpoint=$(curl -s "$ROOT/.well-known/smart-configuration" | python -I -c 'import json,sys; print(json.load(sys.stdin)["token_endpoint"])')
  local assertion
  assertion=$(powershell -NoProfile -ExecutionPolicy Bypass -File "$DIR/../New-ClientAssertion.ps1" -TokenEndpoint "$endpoint" | tr -d '\r\n')
  local out="$OUT/token.json"
  local code
  code=$(curl -s -o "$out" -w '%{http_code}' -X POST "$endpoint" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "scope=$scope" \
    --data-urlencode "client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer" \
    --data-urlencode "client_assertion=$assertion")
  echo "== token for '$scope' -> $code $(python -I -c 'import json,sys; d=json.load(open(sys.argv[1])); print({k:v for k,v in d.items() if k!="access_token"})' "$out")"
  TOKEN=$(python -I -c 'import json,sys; print(json.load(open(sys.argv[1])).get("access_token",""))' "$out")
}

# 1. Subscription: br-provider's dedupe search, then create (its SUBSCRIPTION_SCOPES).
token "system/Subscription.cruds"
call sub-search GET "$BASE/Subscription?url=http%3A%2F%2Fprovider.example.org%2Fapi%2Fpas%2Fnotifications&status=requested,active"
call sub-create POST "$BASE/Subscription" "$DIR/subscription.json"

# 2. Submit (its PAS_SCOPES): the request as built (H2012, pended), then the same with H0019 and no modifiers,
#    which this plan's rules leave as not required.
token "system/Claim.crus system/ClaimResponse.rs"
STAMP=$(date +%s)
sed -e "s/REPLAY-CLAIM-0001/REPLAY-CLAIM-$STAMP-1/" -e "s/TXN-REPLAY-0001/TXN-REPLAY-$STAMP-1/" "$DIR/request.json" > "$OUT/request-h2012.json"
call submit-h2012 POST "$BASE/Claim/\$submit" "$OUT/request-h2012.json"
sed -e 's/H2012/H0019/g' -e "s/REPLAY-CLAIM-0001/REPLAY-CLAIM-$STAMP-2/" -e "s/TXN-REPLAY-0001/TXN-REPLAY-$STAMP-2/" -e 's/c1a4e2f0-0000-4000-8000-000000000001/c1a4e2f0-0000-4000-8000-000000000002/g' "$DIR/request.json" > "$OUT/request-h0019.json"
call submit-h0019 POST "$BASE/Claim/\$submit" "$OUT/request-h0019.json"

# 3. Inquire (query by example, wildcard item).
call inquire POST "$BASE/Claim/\$inquire" "$DIR/inquiry.json"

# 4. The read br-provider falls back to when it has no context for an inquiry.
CRID=$(python -I -c '
import json,sys
d=json.load(open(sys.argv[1],encoding="utf-8"))
for e in d.get("entry",[]):
    r=e.get("resource",{})
    if r.get("resourceType")=="ClaimResponse": print(r.get("id") or ""); break
' "$OUT/submit-h2012.json" 2>/dev/null)
call claimresponse-read GET "$BASE/ClaimResponse/${CRID:-unknown}"
