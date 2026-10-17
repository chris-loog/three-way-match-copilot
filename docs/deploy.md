# Deploying

The app runs on Azure: a web app on Container Apps, a Function app for processing, Azure SQL, Storage, Document Intelligence and Azure OpenAI. Everything authenticates with one managed identity; there are no keys, connection strings with passwords, or secrets in the repository or in GitHub.

```
push to main --> CI (build, test) --> Deploy workflow
                                         |- OIDC login to Azure (no secret)
                                         |- build web image tagged with the commit, push to the registry
                                         |- Container App moves to the new image
                                         |- Function app gets the new package
                                         |- smoke test: GET /health must return 200
                                         '- migration bundle uploaded as an artifact (never applied by the workflow)
```

## First time

1. Create the base resources: `infra/setup.sh` (already done for this project).
2. Create the deploy resources: `bash infra/setup-phase9.sh`. It creates the managed identity and its roles, the registry, the Container Apps environment and app, the Function app, and the GitHub OIDC app registration, then sets the repository variables.
3. Grant the identity database access (the one step `az` cannot do): run `db/grant-identity.sql` in the Portal query editor as the Entra admin. The identity gets `db_datareader` and `db_datawriter` only, so the running app cannot change the schema.
4. Apply the migrations: `bash infra/migrate.sh`.
5. Push to `main`. The deploy workflow runs after CI.

## Database changes

The workflow never applies migrations: its identity has no rights on the database, on purpose. When a commit adds a migration:

```bash
az login
bash infra/migrate.sh                 # builds a bundle from your working copy and applies it as you
# or apply the bundle the workflow built for that commit:
bash infra/migrate.sh ./efbundle
```

Run it before the deploy, or right after. If you forget, `/health` reports the database as unhealthy ("migrations that are not applied") and the smoke test fails, so the mistake shows up at once.

## Rolling back

Run the **Deploy** workflow by hand (Actions, Deploy, Run workflow) and enter the earlier commit in `ref`. It rebuilds that commit, moves the Container App and the Function back, and runs the smoke test. If the bad commit changed the schema, the earlier code must still work with the newer schema (add columns before using them, remove them a release later).

Container Apps also keeps the previous revision for a while: `az containerapp revision list -g rg-three-way-match -n ca-three-way-match -o table`.

## What the smoke test checks

`GET /health` is open to anyone and returns 200 only when the database answers with every migration applied, the `incoming` container can be read, and the `documents` queue exists. A deploy that passed CI but cannot reach its own database or storage (a missing role, a wrong setting) fails here instead of being found by a visitor. The response names the failed check and a fixed sentence, never an exception text.

## Cost and teardown

The registry (Basic) costs about 5 euro a month. Container Apps scale to zero (the first request after idle waits for a cold start of some seconds), the Function app is Flex Consumption, SQL is the free serverless offer, and the rest stays inside free grants at demo traffic. To remove everything: `az group delete -n rg-three-way-match`, then delete the app registration `gh-three-way-match-deploy`.
