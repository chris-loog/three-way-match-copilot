# Evaluation

`dotnet run --project tools/DocGen -- eval` runs every dataset document through extraction (cached by file hash), matching and the rules, with the same code as the pipeline. The notebook `evaluation.ipynb` turns the output into the tables and charts used in the README.

## Run it

```bash
export THREEWAYMATCH_DI_ENDPOINT=https://di-three-way-match.cognitiveservices.azure.com/
az login

# 1. Delivery-note models, dev split (tune and compare here)
dotnet run --project tools/DocGen -- eval --split dev --tag dn-layout                                   # prebuilt-layout + rules
dotnet run --project tools/DocGen -- eval --split dev --tag dn-neural   --dn-model delivery-note-neural-v1
dotnet run --project tools/DocGen -- eval --split dev --tag dn-template --dn-model delivery-note-template-v1

# 2. The run that is reported on dev (with the model you picked)
dotnet run --project tools/DocGen -- eval --split dev --tag dev --dn-model delivery-note-neural-v1

# 3. Once, at the end: the test split
dotnet run --project tools/DocGen -- eval --split test --tag test --dn-model delivery-note-neural-v1
dotnet run --project tools/DocGen -- eval --split test --tag dn-layout-test
dotnet run --project tools/DocGen -- eval --split test --tag dn-neural-test   --dn-model delivery-note-neural-v1
dotnet run --project tools/DocGen -- eval --split test --tag dn-template-test --dn-model delivery-note-template-v1
```

Useful flags: `--cache-only` (never call Azure; a document without a cached result counts as unreadable), `--interval 0` (no pause between calls, for the paid tier), `--threshold 0.80`, `--thresholds 0.6,0.7,0.8,0.9`.

Each run writes `eval/out/<tag>/results.csv` (one row per document), `lines.csv` (one row per invoice line) and `sweep.csv` (confidence threshold against straight-through rate and wrong approvals). They are git-ignored; the notebook is committed with its outputs.

## Test-split discipline

Only documents of the chosen split are read by Document Intelligence. The other split is read as its printed text, so the shared state (what earlier invoices already billed, earlier invoice numbers) is complete without touching those files. Run dev as often as you like. Run test once. A later test run is reported as "v2".

## Notebook

```bash
python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
cd eval && ../.venv/bin/jupyter nbconvert --to notebook --execute --inplace evaluation.ipynb
```

Charts are saved to `docs/img/`.

## Processing time

Not measured by this run (it reads from the cache). Upload about ten documents through the real pipeline and use the Application Insights query in section 9 of the notebook.
