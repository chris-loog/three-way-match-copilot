# Design decisions

The decisions that shaped the system, each with what else was considered and what the choice costs. The full dated log is at the end. The [README](../README.md) has the summary.

## 1. The matching engine is a pure function over a snapshot

**Decided:** `Match(input, snapshot)` takes plain objects and returns an outcome. A loader in Infrastructure reads the ERP tables once per document and hands over the snapshot. The engine and the rules never query anything.

**Alternatives:** rules that query the database themselves (simpler to write, one query per rule per line); repository interfaces injected into each rule.

**Why:** every rule is tested with a hand-built mini-ERP in milliseconds, with no database and no mocks. The same code runs in the pipeline, in the offline simulation over 258 documents, and in the evaluation, so the numbers in the README come from the code that ships.

**Cost:** the loader reads more data than a single document needs (all suppliers, items and orders). Fine at this size; a real ERP would need a loader that fetches only the relevant orders.

## 2. The language model explains and never decides

**Decided:** approve, hold or reject comes from thirteen rules and a fixed precedence (rejected, on hold, needs review, auto-approved). The model writes an explanation and a draft message after the decision.

**Alternatives:** let the model judge the document; use the model as a second opinion that can veto.

**Why:** a decision has to be testable, auditable and cheap to re-run. A model's judgement is none of those, and a wrong auto-approval is the one failure that costs money.

**Cost:** the explanation can read well and still be wrong about the cause, so the cause field must say "unclear" when the facts do not show one, and a person reads it before acting.

## 3. Guards in plain code, then a template

**Decided:** every number in the answer must appear in the facts (any notation), every code must be one of the findings, every blocking finding must be named for the reviewer. A failing answer is retried once with the reasons, then replaced by fixed text built from the facts.

**Alternatives:** a second model as judge; trust the schema alone.

**Why:** the guards are deterministic and testable, and the reviewer always sees a correct text, even when the model is down. A judge model adds cost and its own errors.

**Cost:** the guards check numbers and codes, not meaning.

## 4. Prompt injection is handled by what goes in

**Decided:** the model never sees document text. It gets a JSON object of facts built from the database. Line descriptions, the one free-text field, are stripped of control characters, cut to 60 characters and sent as a data value.

**Alternatives:** send the extracted text and tell the model to ignore instructions in it; filter for suspicious phrases.

**Why:** a filter loses to the next phrasing. With no document text, no tools and no decision to take over, there is little left for an injected instruction to do.

## 5. Confidence is the model's, backed by arithmetic and the ERP

**Decided:** field confidence stays what Document Intelligence reported. A low value on the gross total or on a line quantity or price is ignored when the invoice reconciles on its own (quantity x price = line, lines = net, net + VAT = gross). A low PO reference is ignored when the supplier was found by VAT number and that order exists for that supplier.

**Alternatives:** lower the threshold; take the lowest word confidence; flag everything below 0.80.

**Why:** the model scored correct values at 0.44, and a collapsed table at 0.002 for values that were all right. Independent arithmetic or a lookup in the ERP is stronger evidence than the score. A misread like `1 292` read as `1.292` never reconciles, so it is still caught.

**Cost:** a misread order number that lands on another real order of the same supplier would pass this check. The line, quantity and price rules still apply.

## 6. Delivery notes: one interface, a configurable reader

**Decided:** one extractor interface with a second method for delivery notes. The model id is a setting. With the layout model, simple rules read the header labels and the table columns (found by header name); with a custom model the same mapper reads its fields. The value found by a rule gets the lowest confidence of the words it was read from.

**Alternatives:** train a custom model first and build nothing until it exists; hard-code the layout rules.

**Why:** it works today, a trained model is a settings change, and the stored raw result lets old documents be re-read with a new mapper. Neural against template models, including the held-out layout, is the comparison to report.

## 7. Idempotency by blob path, not by file hash

**Decided:** one row per uploaded blob path. The hash is only an index.

**Alternatives:** a unique hash.

**Why:** a retried queue message finds its row and resumes. A unique hash would make a resent invoice vanish silently, and the exact-duplicate rule, the only route to rejection, could never fire.

## 8. Every stage is saved and can be skipped

**Decided:** received, extracted and decided are saved separately; each stage checks what is stored. Temporary failures (service busy, database unreachable) are thrown so the queue retries; permanent ones end as `Failed` with the error visible.

**Why:** the queue can deliver twice and a worker can crash halfway. Retrying a corrupt PDF would only use quota.

## 9. Temporal tables for the audit trail

**Decided:** the decision and the reviewer's override live in SQL temporal tables.

**Alternatives:** an `AuditLog` table written by the application.

**Why:** the history comes from the database for every change, including ones the code forgot. It is also a good place to learn what SQL Server does for you.

## 10. Typed extraction tables plus the raw JSON

**Decided:** typed `ExtractedDocument` and `ExtractedLine` columns, and the unmodified result next to them.

**Alternatives:** a generic field-name/value table.

**Why:** matching queries stay simple, and mapping fixes or reviewer corrections re-apply on top of the stored result without calling Azure again.

## 11. Units convert only through the supplier's item data

**Decided:** a base unit on the item and a conversion factor on the supplier item. Solid cubic metres and stacked cubic metres are never converted; a mix-up is `UOM_MISMATCH`.

**Alternatives:** a global unit dictionary with conversions.

**Why:** the stacked and solid cubic metre differ by a factor that depends on the product. Converting silently is how a quantity error turns into a payment.

## 12. The evaluation cannot touch the test split by accident

**Decided:** Document Intelligence only reads the chosen split; the other split is read as its printed text so the shared state (what is already billed, earlier invoice numbers) stays complete. The confidence threshold is chosen on dev and applied once to test.

**Alternatives:** run everything and filter the rows afterwards.

**Why:** a dev run then cannot leak the test files into a decision, and "test is run once" is true by construction.

## 13. Synthetic documents with ground truth

**Decided:** generate the ERP data and 258 documents (five layouts, one held out entirely), inject discrepancies on purpose, and degrade about a third into scans and photos.

**Alternatives:** public invoice datasets.

**Why:** matching needs to know which order a document belongs to and which problem was injected. A public set has neither. The cost is that recall on injected errors is partly circular, which the README says.

## 14. Deployment: no secrets, a schema the app cannot change

**Decided:** one managed identity for the web app and the Function; GitHub logs in with OIDC; the app's database user can read and write data only; migrations are a bundle an administrator applies. The deploy workflow builds the bundle but never applies it, and the `/health` smoke test fails when migrations are pending.

**Alternatives:** connection strings in Key Vault; a deploy identity with database rights; migrations applied by the app at startup.

**Why:** nothing to rotate or leak, and a compromised app cannot drop tables.

**Cost:** one shared identity means the web app can do whatever the Function can. Separate identities would be tighter.

## 15. Rollback is a deploy of an older commit

**Decided:** the deploy workflow takes a commit as input, rebuilds it and runs the same smoke test.

**Why:** one path for deploy and rollback. The catch is the database: older code must work with the newer schema, so schema changes go in two steps (add a column, use it, remove the old one a release later).

## 16. Explanations on demand

**Decided:** a button on the detail page, throttled per address, not a call for every stopped document.

**Why:** each call costs money, the demo site is public, and a correction would make an eager explanation stale. The stored text is deleted when the document is matched again.


## Full log

Every decision with a date, in the order it was made. Rows about how the work was organised are left out.

| Date | Decision | Why |
|---|---|---|
| 2026-10-06 | Repo private until README + results are ready, then public | A half-finished public repo makes a weaker impression |
| 2026-10-06 | .NET 10 + `.slnx` | Consistent with Steel Defect Triage |
| 2026-10-06 | Web UI: Razor Pages | Fits an inbox/detail app with less ceremony than MVC |
| 2026-10-06 | `Directory.Build.props` + central package management | Code-quality defaults |
| 2026-10-06 | Develop directly against Azure SQL from step 0.1 (with retry on failure) | One database, no local setup |
| 2026-10-06 | SQL schemas `erp.` (mock ERP) and `ap.` (app) | Shows the boundary to a real ERP |
| 2026-10-06 | Typed `ExtractedDocument` + `ExtractedLine` + raw JSON instead of a generic `ExtractedField` table | Matching queries stay simple |
| 2026-10-06 | Seed data with Bogus + fixed seed, as a `seed` command in `tools/DocGen`; re-runnable on `erp.` only | Reproducible data; one tool owns all test data |
| 2026-10-06 | Fictional IBANs use an unassigned bank code with a valid checksum | No generated account can be real |
| 2026-10-06 | Seeder fills ERP data only; duplicate invoices come from Phase 2 | Keeps the seeder simple |
| 2026-10-06 | Seeded goods receipt = warehouse booking; delivery notes checked against PO + receipt; new rule `DELIVERY_NOTE_MISMATCH` | Matches how receiving works in practice |
| 2026-10-06 | 5 layouts, one per supplier (two suppliers share one); ~3 Estonian, ~2 English | Realistic supplier mix |
| 2026-10-06 | Mostly exactly one injected discrepancy per invoice | Clear evaluation of what was caught |
| 2026-10-06 | Ground truth JSON per document + `manifest.csv` | Easy evaluation in Python |
| 2026-10-06 | Stratified 70/30 split + one supplier layout held out entirely | Tests behaviour on an unseen layout |
| 2026-10-06 | Generated documents are git-ignored (`data/generated/`) | Reproducible from the fixed seed |
| 2026-10-06 | Python 3.12 shared venv; PyMuPDF + augraphy; scans as PDF, photos as JPG | Compatibility, no system dependencies, realistic formats |
| 2026-10-06 | Quality levels recorded in `manifest.csv`; fixed seed; real photos in test split only | Results per quality level; reproducible |
| 2026-10-06 | Low-confidence → review counts as correct; only wrong auto-approvals are failures | Matches the main KPI: safe automation |
| 2026-10-06 | Interface + `ExtractedInvoice` in Domain, Document Intelligence client in Infrastructure | Matching engine testable without Azure |
| 2026-10-06 | Query fields add-on (regex fallback) for IBAN and registry number | Not reliably in the prebuilt model |
| 2026-10-06 | Per-field confidence, key-field list, threshold 0.80 → `LOW_CONFIDENCE` | Safe automation |
| 2026-10-06 | Feedback loop: reviewer corrections stored in `ap.FieldCorrection`; used for accuracy stats, learned item aliases and (stretch) retraining | Improve accuracy over time; realistic AP practice |
| 2026-10-06 | Cache raw results by content hash; tests use saved JSON; CI never calls Azure | Save quota, fast tests |
| 2026-10-06 | Budget €100 of €175 credits; F0 for dev, S0 allowed when limits get in the way | Comfortable budget; don't over-optimise |
| 2026-10-07 | Normalisation as pure Domain functions, written test-first | Easy to test; TDD practice |
| 2026-10-07 | Parse raw text ourselves and compare with Document Intelligence's typed value | Estonian formats get misread |
| 2026-10-07 | `Supplier.DocumentCulture` decides ambiguous numbers and dates | Like supplier language/format settings in an ERP |
| 2026-10-07 | Unit synonyms in a code dictionary; `tm` ≠ `rm` never converted | Simple now; timber-specific correctness |
| 2026-10-07 | Invalid IBAN checksum → `LOW_CONFIDENCE`; valid but changed → `BANK_ACCOUNT_CHANGED` | Avoid false fraud alarms from bad scans |
| 2026-10-07 | `TryParse`/result type instead of exceptions | Unreadable fields go to review, not crash |
| 2026-10-07 | Label 7 documents per dev layout (28 total) | Enough variety per layout |
| 2026-10-07 | Separate `training` container; label files copied into the repo | Reproducible training |
| 2026-10-07 | Versioned model IDs from config; model ID stored per extracted document | Traceability |
| 2026-10-07 | Header matching with candidate-PO search when the PO reference is missing | Mirrors how accountants search by hand |
| 2026-10-07 | Line cascade: supplier code → item code → learned alias → fuzzy (FuzzySharp) | Reviewer corrections pay off |
| 2026-10-07 | Fuzzy matches never auto-approve; confirmed matches become aliases | Safety first |
| 2026-10-07 | Best-fit PO line for repeated items; ties → review; charge lines via keyword list | Common real-world invoice patterns |
| 2026-10-07 | Store match method + confidence per line (`ap.LineMatch`) | Explainability in UI and LLM |
| 2026-10-07 | Matching engine is pure (data snapshot in, result out) | Fully unit-testable |
| 2026-10-07 | Fourth outcome **OnHold** for partial deliveries, re-checked when goods arrive | Real AP workflow |
| 2026-10-07 | New rules `POSSIBLE_DUPLICATE` (Warning) and `PRICE_BELOW_PO` (Info) | Resent invoices; complete price picture |
| 2026-10-07 | Bank change: "verified by phone" checkbox + note; four-eyes = README next step | Real control, simple to build |
| 2026-10-07 | Findings carry amount at stake; queue sorted by it | How AP teams prioritise |
| 2026-10-07 | Safety test: no injected-discrepancy document is ever AutoApproved | Main KPI as a test |
| 2026-10-07 | Blob → Event Grid → Storage Queue → queue-triggered Function | Local dev without tunnel; built-in retries + poison queue |
| 2026-10-07 | Type by upload folder | Simple; classifier is a stretch goal |
| 2026-10-07 | No blob moves; status in `ap.Document`; `processed`/`failed` containers removed | Copy+delete can fail halfway; DB is the source of truth |
| 2026-10-07 | `rowversion` optimistic concurrency on PO lines | Prevents double-invoicing races |
| 2026-10-07 | Daily timer Function re-checks OnHold documents | Partial deliveries resolve automatically |
| 2026-10-07 | Application Insights with per-document correlation | Debugging + processing-time metric |
| 2026-10-07 | Entra ID sign-in + demo mode (fixed reviewer, reset, rate limits) | Real auth, but recruiters can still try it |
| 2026-10-07 | Pages: Inbox, Detail, Upload, Failed, Dashboard (Chart.js) | Covers the full AP workflow |
| 2026-10-07 | User-delegation SAS for document preview | Consistent with no-keys rule |
| 2026-10-07 | Field highlighting on the document = stretch goal | High wow factor, ~1 day of work |
| 2026-10-07 | Saving a correction re-runs matching without re-extraction | Immediate feedback for the reviewer |
| 2026-10-07 | Razor Pages + htmx + Bootstrap; `WebApplicationFactory` tests | Modern, light, no SPA learning curve |
| 2026-10-07 | LLM explanations precomputed for NeedsReview/OnHold only, stored, regenerated after corrections | Instant UI, minimal cost |
| 2026-10-07 | Facts include supplier history | More useful explanations |
| 2026-10-07 | Draft supplier message in the supplier's language; everything else English | Real value for an accountant |
| 2026-10-07 | Number guard + code guard + Block-mention check, retry once, then template fallback | LLM output is always safe to show |
| 2026-10-07 | Raw document text never sent to the LLM | Prompt-injection defence |
| 2026-10-07 | Role for Azure OpenAI = Cognitive Services OpenAI User | Correct least-privilege role |
| 2026-10-07 | C# `eval` runner + Python notebook; pipeline timing from App Insights | Same code as production, repeatable |
| 2026-10-07 | Added recall per rule, false alarms, line matching, cost, p50/p95, counts | Complete, honest picture |
| 2026-10-07 | Confidence threshold tuned on dev, applied once to test | Avoids overfitting to the test set |
| 2026-10-07 | Notebook committed with outputs; limitations stated in README | Readable on GitHub; honesty |
| 2026-10-07 | One user-assigned managed identity for web + Function | Assign roles once |
| 2026-10-07 | No secrets → no Key Vault | Identity everywhere; strong security story |
| 2026-10-07 | Deploy workflow ends with a `/health` smoke test | Catch broken deploys immediately |
| 2026-10-07 | One public demo environment; rollback via revisions | Staging not worth it for a portfolio project |
| 2026-10-07 | `docs/decisions.md` published; gitleaks scan before going public | Shows judgement; no leaked secrets |
| 2026-10-07 | Forecasting crash course hands-on with Elering data, in a separate repo/folder | Real Estonian data; keeps the project focused |
| 2026-10-10 | Project name **Three-Way Match Copilot**, repo `three-way-match-copilot`, .NET namespace `ThreeWayMatch` | Uses the standard AP term; matches the Steel Defect Triage Copilot naming |
| 2026-10-10 | Azure OpenAI in **France Central** with `gpt-4.1-mini` as **GlobalStandard** | Sweden Central: no account quota; Standard SKU quota is 0. Synthetic data only, so global processing is acceptable |
| 2026-10-10 | Event Grid system topic `evgt-three-way-match`, subscription filtered to `incoming/` BlobCreated events | Only uploads trigger processing |
| 2026-10-10 | Learned aliases in `ap.ItemAlias` (points at `erp.SupplierItem`), not columns on `SupplierItem` | The app never writes to ERP tables; `Supplier`/`SupplierItem` stay a clean mock ERP |
| 2026-10-10 | Enums stored as text; money `decimal(19,4)`, quantities `decimal(18,4)`, VAT rate `decimal(5,4)`; business dates as `date`, timestamps as `datetimeoffset` | Readable data in SQL and the review UI; no float anywhere |
| 2026-10-10 | All foreign keys `NO ACTION` except where a parent owns its children; `LineMatch` ↔ `GoodsReceiptLine` is a join table (`ap.LineMatchGoodsReceiptLine`) | No accidental cascade deletes; avoids SQL Server "multiple cascade paths" |
| 2026-10-10 | `ExtractedDocument` stores `MinKeyFieldConfidence` plus `FieldConfidenceJson` (per field) and `RawJson` | Per-field confidence from 3.1 without a generic field table |
| 2026-10-10 | Seeder splits a pure `ErpDataBuilder` (seed 20261010, fixed "today" 2026-10-09) from `ErpSeeder` (EF writes, one transaction, retry-safe); `--dry-run` runs offline | Same data every run; the data logic is checkable without Azure |
| 2026-10-10 | Fictional IBANs use bank code `04` (not in the Finantsinspektsioon codes found: 10, 16, 22, 42, 75, 77, 96) | Plan rule: valid checksum, unassigned bank. The check covers major banks only, so it is "very unlikely real", not proof |
| 2026-10-10 | Seeder refuses to run if `ap.MatchResult`, `LineMatch` or `ItemAlias` hold rows; does not reseed identity values | Never orphans `ap.` data; business keys (PO/receipt numbers) are stable, surrogate IDs are not |
| 2026-10-10 | 61 items: 50 goods, 5 transport services, 5 delivery-charge items (one per material supplier); 4 Estonian + 2 English suppliers | Charge-line rules (5.1) need real charge lines on POs; matches 3 Estonian + 2 English layouts |
| 2026-10-10 | `generate` rebuilds the ERP data from the seeded `ErpDataBuilder` instead of reading Azure SQL | Same data as the seeded database (deterministic), so documents can be generated offline |
| 2026-10-10 | Held-out layout = L3 (glue and finishes, Estonian); L4 is shared by hardware and transport | Dev keeps 2 Estonian + 2 English layouts for the delivery-note labelling in 4.1 |
| 2026-10-10 | Exact resends are byte copies of the original PDF (same hash); PDF metadata dates are fixed | Tests the content-hash duplicate check; generated files are reproducible |
| 2026-10-10 | `PARTIAL_DELIVERY` invoices: 6 on unreceived orders, 4 billing a partly delivered order in full (these replace that delivery's normal invoice) | Avoids double-billing one delivery; both are real AP patterns |
| 2026-10-10 | Split: at least one dev and one test document per (kind, main finding) group; held-out layout all test | Every finding type is measurable on both sides |
| 2026-10-10 | Ground truth stores the true link (PO, receipt) even when the printed PO reference is blank or wrong | Scoring can tell "no reference printed" from "matched wrongly" |
| 2026-10-10 | Bug fixed: supplier weights for PO generation must sum to 1 for Bogus `WeightedRandom` (all 120 POs were timber) | Found by the per-supplier summary line now printed by `seed --dry-run` |
| 2026-10-10 | Degrade writes a separate `data/dataset` (clean copies + degraded files) from `data/generated`, never in place | A second run cannot double-degrade; the clean source stays intact |
| 2026-10-10 | Levels 12 % `scan_light` / 8 % `scan_heavy` / 10 % `photo`, picked by systematic sampling over (layout, split, finding) | Even coverage of layouts and error types; exactly 30 % |
| 2026-10-10 | Scans are image-only PDFs, photos are JPG; exact resends inherit the original's file and quality | Forces OCR like a real scan; keeps resends byte-identical |
| 2026-10-10 | Replaced augraphy `Folding`, `DirtyRollers`, `BadPhotoCopy` with numpy versions (`fold_lines`, `roller_streaks`, `toner_specks`); the "RECEIVED" stamp and photo effect are also custom | The three augraphy effects differ run to run even with Python, numpy, OpenCV and numba seeds fixed (parallel numba code) |
| 2026-10-10 | Python 3.14 in a shared `.venv` (not 3.12) | Plan allows newer if everything installs cleanly; it did |
| 2026-10-10 | `real_photos.py suggest/register` for the 3-5 `photo_real` documents (test split only) | Photos are the one manual step; the tooling around them is automated |
| 2026-10-10 | Cache raw Document Intelligence JSON (not the mapped object), key = SHA-256 + model id; mapping always re-runs on read | Mapping fixes need no new Azure calls; a different model never reuses another model's result |
| 2026-10-10 | `IAnalysisClient` (bytes in, raw JSON out) separate from `InvoiceMapper` (JSON in, `ExtractedInvoice` out) | Only one thin class touches Azure; the rest is testable offline |
| 2026-10-10 | Regex fallback values get the lowest confidence of the OCR words they cover; a missing key field counts as confidence 0 | A smudged or absent IBAN/PO reference must trigger LOW_CONFIDENCE, not pass silently |
| 2026-10-10 | `extract` defaults to 10 documents and 3 s between calls | F0 limits: 20 calls/min, 500 pages/month; protects the free quota |
| 2026-10-10 | `Azure.Core` pinned to 1.53.0 centrally | Azure.Identity and the Document Intelligence SDK ask for different versions |
| 2026-10-10 | One test project `ThreeWayMatch.Domain.Tests` covers Domain and Infrastructure (as named in the plan); CA1707 off under `tests/` | Fewer projects; test names are readable sentences |
| 2026-10-11 | One `Normaliser` static class plus `ExtractionCrossCheck`; amounts: culture decides single ambiguous separators, grouped digits must be groups of three, a leading zero before a group is refused | Ambiguity goes to review instead of becoming a silent wrong number |
| 2026-10-11 | `CheckIban` returns Valid / InvalidChecksum / Malformed; only a valid mismatch is BANK_ACCOUNT_CHANGED | Misread vs fraud, as decided on 2026-10-07 |
| 2026-10-11 | Cross-check sets confidence to 0 (not a separate flag) when our parse disagrees with the model | One mechanism (`LowConfidenceFields`) drives LOW_CONFIDENCE; fewer moving parts |
| 2026-10-11 | `ExtractedInvoice` is a record | Cross-check returns a corrected copy; the original is untouched |
| 2026-10-11 | Engine takes a `MatchInput` (not `ExtractedInvoice`); `MatchInput.FromInvoice` adapts; delivery notes will get their own adapter | One engine for both document types; no Azure or extraction types in matching |
| 2026-10-11 | Snapshot uses the existing entities (loaded with their relations) plus an invoiced-quantity-per-PO-line map | No duplicate model; the loader just `Include`s |
| 2026-10-11 | A concern list (fuzzy supplier, fuzzy line, suggested PO, ambiguous line) instead of a "needs review" boolean | 5.2 maps each to a reason the reviewer and the LLM can quote |
| 2026-10-11 | Uncoded charge lines are never fuzzy-matched: matched to the order's charge line by keyword, else reported as not on the order | Avoids matching "Fuel surcharge" to a product |
| 2026-10-11 | Fuzzy match needs score >= 85 and a lead of >= 3 over the runner-up; supplier-name match >= 90 and lead >= 5 | A description that fits two items is better matched to neither |
| 2026-10-11 | PO candidate search: every goods line must resolve and fit the open quantity; one unmatched line means no suggestion | The safe answer when the document does not look like one order |
| 2026-10-11 | Closed PO lines are skipped when an open line of the same item exists; equal open quantities are a tie | Real "same item, two delivery dates" orders |
| 2026-10-11 | Unit conversion: supplier unit uses the factor, base unit needs none, anything else is `Mismatch`, unreadable is `Unknown` | `tm` vs `rm` and unknown units reach the UOM rule instead of being guessed |
| 2026-10-11 | NO_PO_REFERENCE is reported whenever the reference is not confirmed (missing, unknown, other supplier), even when a PO is suggested from the lines; the suggestion lets the other rules run | Matches the catalogue ("missing or unknown") and the generator; a suggestion alone never approves |
| 2026-10-11 | One quantity analysis feeds QTY_OVER_INVOICED and PARTIAL_DELIVERY: more than received is "wait" only if goods are still expected and the order is not exceeded, else over-invoiced | The two rules cannot disagree; early invoices wait, wrong ones go to review |
| 2026-10-11 | OnHold only when PARTIAL_DELIVERY is the sole Warning-or-worse finding and the match made no guess | Waiting does not fix a bank change or a wrong price |
| 2026-10-11 | Delivery notes are compared exactly with the booked quantity (no tolerance), only when a receipt with that note number exists | A warehouse booking 9 against a note of 10 is a problem; arrival order must not matter |
| 2026-10-11 | A PO reference that is absent does not count as an unreadable key field; one that is present but doubtful does | NO_PO_REFERENCE already reports absence; avoids a redundant second finding |
| 2026-10-11 | An invoice with no extracted lines is LOW_CONFIDENCE | Otherwise nothing could be checked and it could be approved on header fields alone |
| 2026-10-11 | POSSIBLE_DUPLICATE is skipped when the two invoices are known to be for different orders | Standard services cost the same every time |
| 2026-10-11 | The safety property runs as an xUnit test over all generated documents (tests reference `tools/DocGen`) and as `DocGen check-rules` | The main KPI is checked on every push in CI |
| 2026-10-12 | `ap.Document` unique on `BlobPath`; `ContentHash` is a plain index (new migration `DocumentUniqueByBlobPath`) | A unique hash made a resent file vanish and the exact-duplicate rule unreachable; a path key still makes retries idempotent and lets a resend be Rejected |
| 2026-10-12 | Delivery notes end as `Failed` ("model comes with 4.1") in the pipeline for now | No delivery-note extractor yet; failing visibly beats guessing |
| 2026-10-12 | Transient errors (429/5xx/timeouts/SQL) rethrow for queue retry; everything else becomes `Failed` with the error text | Retrying a corrupt PDF only burns quota |
| 2026-10-12 | An approved invoice claims its PO lines by touching them and comparing row versions with the snapshot; the loser retries (3 attempts) | Makes `rowversion` do the job the plan gave it |
| 2026-10-12 | Approved quantity = auto-approved or reviewer-acted line matches, converted back to the PO unit | Matches "invoiced = sum of approved, matched invoice lines" |
| 2026-10-12 | `Azure.Core` pin raised to 1.63.0; `ExtractionOptions` deleted (`DiscrepancyOptions.ConfidenceThreshold` is the one threshold); cache temp file uses a unique name | Application Insights needs the newer Core; review findings 1 and 2 |
| 2026-10-12 | Review UI uses Bootstrap's reboot only plus a hand-written stylesheet (no Bootstrap components) | The design brief asked for a distinct look; reboot gives a sane base without the default component styling |
| 2026-10-12 | `DocumentMatcher` split out of `DocumentProcessor`; the web app registers only matching (`AddMatching`) | The UI re-matches after a correction without owning a Document Intelligence client |
| 2026-10-12 | Corrections are stored in `ap.FieldCorrection` and re-applied on top of the stored raw JSON on every match; corrected fields have no source text and confidence 1; amounts use a point, dates `yyyy-MM-dd` | Mapping fixes and corrections both survive a re-match without calling Azure |
| 2026-10-12 | A document a reviewer has approved is never matched again (timer and corrections skip it) | The human decision must not be overwritten by a machine re-run |
| 2026-10-12 | Demo mode = fixed reviewer via a custom auth handler; Entra ID via Microsoft.Identity.Web when off; upload limited to 10 files a minute per address and 10 MB each | Recruiters can try the public site; one visitor cannot fill the storage account |
| 2026-10-12 | htmx used as `hx-boost` on the body plus a debounced search that swaps only the list | Smooth navigation and partial updates without a SPA |
| 2026-10-12 | Identity packages (`Microsoft.IdentityModel.*` 8.23.0, `Microsoft.Identity.Client` 4.90.1) pinned centrally | Identity.Web and Azure.Identity asked for different versions; the conflict warning is an error here |
| 2026-10-06 | CI = build + test on push to `main`; deploy only in Phase 9 | Keep CI simple until there's something to deploy |
| 2026-10-15 | `InvoiceReconciliation.Reconciles`: if qty x price = line total, lines = net and net + VAT = gross (all figures present, none distrusted by the cross-check), `LowConfidenceFields(threshold, tolerance)` ignores low `GrossTotal` and line quantity/price. Stored confidences stay unchanged | Independent arithmetic backs the values (L5 gross 0.44, collapsed table 0.002 on inv-0018); a misread like `1 292` -> 1.292 never reconciles |
| 2026-10-15 | `LowConfidenceRule` drops `PurchaseOrderReference` when the supplier was matched by VAT number and the printed PO exists for that supplier (decision: assessor; done in the rule because the match is already in the context) | A PO number checked against the ERP is like a checksum. Residual risk: a misread that lands on another real PO of the same supplier; line/quantity/price rules still apply |
| 2026-10-15 | Confidence threshold stays 0.80; re-measured on dev/test after the changes instead of tuned | 18 documents are too few to fit a number; tune on dev only, report on test |
| 2026-10-15 | Mapper test fixture is now the real saved result for inv-0018 (`Fixtures/real-invoice-inv-0018.json`); the hand-built one became `pipeline-invoice.json` for the pipeline/web tests | They need a supplier/PO world that fits the fixture; the mapper tests should pin real service output |
| 2026-10-15 | Training set picked by `tools/training/pick_training_set.py`: 7 dev delivery notes per layout (L1, L2, L4, L5), up to 2 light scans, 1 photo, 1 heavy scan, rest clean; L3 stays held out | Reproducible, covers degradations, matches the 4.1 plan |
| 2026-10-15 | Delivery notes: one `IDocumentExtractor` with a second method; the model id is configuration, default `prebuilt-layout` + rules until the custom model exists. The mapper picks custom fields when the result has them, otherwise the rules | Works today, switches by setting, no migration |
| 2026-10-15 | Rules fallback confidence = lowest word confidence of the value's words; an unparseable quantity gets 0 | Same threshold as the invoice path |
| 2026-10-15 | The buyer's own VAT number (`EE100000001`) is a constant in `DeliveryNoteMapper`; supplier VAT is the first other one on the page | Every note prints both; the company is fictional |
| 2026-10-15 | Delivery notes do not claim PO lines on approval and are excluded from "already invoiced"; reviewer corrections stay invoice-only | A note judges the warehouse booking, not billing; corrections for notes are out of scope |
| 2026-10-15 | Explanations are generated on demand (button, throttled 10/min per address), not at processing time | Each call costs money, the site is public, and corrections would make an eager one stale anyway |
| 2026-10-15 | One `MatchExplanation` row per document, deleted when the document is matched again; template fallback after two failed attempts or a model error | An explanation must never describe an old state; the screen always has a correct text |
| 2026-10-15 | Guards: number set, code set, Block findings named for the reviewer, draft length; plain code, no judge model | Deterministic and testable; a second model would add cost and its own errors |
| 2026-10-15 | Facts carry supplier history as counts (invoices in 6 months, earlier findings by code); no bank-change date | No change history is stored for the supplier master; counts are what exists |
| 2026-10-15 | `System.Security.Cryptography.Pkcs` and `.Xml` pinned to 10.0.13 | Azure.AI.OpenAI 2.1.0 pulls in vulnerable versions; warnings are errors here |
| 2026-10-15 | Eval simulates with the pipeline's assessor over a rebuilt ERP, not the database; documents of the non-chosen split are read as printed text; real file hashes drive the duplicate check | Repeatable and fast; the test files are never read in a dev run; the copies the degrade script made are judged as the pipeline would |
| 2026-10-15 | A document that cannot be read counts as not approved in every metric; the sweep counts it too | An unreadable document must never look like a success |
| 2026-10-15 | Eval outputs are git-ignored CSVs; the notebook is committed with outputs; cost uses list-price constants to edit | CSV rule already in .gitignore; prices change |
| 2026-10-17 | First dev eval with prebuilt-layout + rules: 50 false alarms of 140, 0 wrong approvals. Three causes fixed: OCR writes "no .:" (regex now allows spaces), OCR reads m³ as mª/mº on scans (both added as cubic metre), prebuilt-invoice omits the PO field although it is printed (regex fallback on the page text). Result: 22 false alarms, 66 % straight-through, 0 wrong approvals | These were real read errors seen in the data, not tuning of numbers; each has a test |
| 2026-10-17 | Remaining dev false alarms are heavy scans and photos (unreadable quantity or number, `m2` read for `m3`, a fuzzy line match): left as they are | Sending a doubtful scan to a person is the intended behaviour |
| 2026-10-17 | Confidence threshold stays 0.80: dev sweep is flat from 0.60 to 0.90 (118 approved, 0 wrong) and drops to 72 false alarms at 0.95 | Nothing to gain by moving it |
| 2026-10-17 | One shared managed identity for web and Function, with Storage Blob Data Owner (the Functions host needs it for leases) | The plan's single identity; separate ones would be tighter and are listed as the trade-off |
| 2026-10-17 | Rollback = run Deploy by hand with an older commit; image built on the runner and pushed with `az acr login` | No ACR Tasks dependency; one path for deploy and rollback |
| 2026-10-17 | ASP.NET data-protection keys persisted to the `dataprotection` blob container | Scale to zero otherwise breaks forms across a cold start; no Key Vault, so keys are not encrypted at rest |
| 2026-10-17 | Phase 9 resources in a new script `infra/setup-phase9.sh`, not appended to `setup.sh` | setup.sh is a reference of what exists; the new one is meant to be run |
