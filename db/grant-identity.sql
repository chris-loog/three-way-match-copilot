-- Run once, in the Azure Portal query editor (or sqlcmd -G) as the Entra admin of the SQL server, connected to sqldb-three-way-match.
-- Gives the shared managed identity read and write on the data, and nothing else: no schema changes, no permissions, no other databases.
-- Schema changes come only from the EF Core migration bundle, which you run as the Entra admin.
CREATE USER [id-three-way-match] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-three-way-match];
ALTER ROLE db_datawriter ADD MEMBER [id-three-way-match];
