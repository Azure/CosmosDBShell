shell-ready = Cosmos DB shell ready.
shell-not_connected_hint = Not connected. Run 'connect <endpoint>' to authenticate, or 'help connect' for more options.
shell-hisory_file_deleted = History deleted.
shell-connect-browser-auth = Authenticating via browser. Please complete the login in the browser window that opens.
shell-connect-devicecode-auth = Browser authentication failed. Falling back to device code authentication.
shell-connect-key-auth = Connecting with account key...
shell-connect-managed-identity-auth = Connecting with managed identity (client ID: { $clientId })...
shell-connect-default-auth = Connecting with DefaultAzureCredential...
shell-connect-static-token-auth = Connecting with externally provided access token (COSMOSDB_SHELL_TOKEN)...
shell-connect-static-token-expiry = Expires in { $timespan } (expiration: { $expiration }).
shell-connect-vscode-credential-auth = Connecting with Visual Studio Code credential...
shell-connect-vscode-credential-fallback = Visual Studio Code credential unavailable, falling back...
shell-connect-devicecode-fallback = Browser authentication failed, falling back to device code authentication...
shell-connect-arm-discovery-failed = Using Cosmos DB data plane.
shell-connect-arm-discovery-ambiguous = Multiple ARM Cosmos DB accounts match the connected endpoint. Reconnect with --subscription and --resource-group, or use --connect-subscription and --connect-resource-group at startup, to specify which account to use. Using Cosmos DB data plane for now.
history-search-reverse = reverse-i-search
history-search-forward = forward-i-search
history-search-failed-reverse = failed reverse-i-search
history-search-failed-forward = failed forward-i-search

yes_char = Y
no_char = N

error = Error:
error-connection_failed = Failed to connect to the Cosmos DB account.
error-emulator_connection_failed =
    Could not reach the Cosmos DB emulator at { $endpoint }.
    Make sure the emulator container is running ('docker ps') and reachable.
    Tip: the Linux emulator exposes a health probe at http://localhost:8080/alive that can be used to verify it is up.
    The Cosmos DB SDKs (including this shell) require HTTPS, but the Linux emulator defaults to HTTP.
    Restart the container with --protocol [https|http], for example:
        docker run -d -p 8081:8081 -p 1234:1234 -p 8080:8080 mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview --protocol https
    Or, if you intentionally started the emulator with the other protocol, try connecting to { $alternate } instead.
    See: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux
error-command-not-found = '{ $command }' is not recognized as an internal or external command, operable program or batch file.
error-command-not-found-suggestion = Did you mean '{ $suggestion }'?
error-command-not-found-connection-string = This looks like part of a connection string. The shell treats ';' as a command separator, so wrap the whole value in quotes, e.g. connect "AccountEndpoint=https://localhost:8081/;AccountKey=...;".
error-unknown-option = Unknown option '{ $option }'.
error-unknown-option-suggestion = Did you mean '{ $suggestion }'?
error-shell-not-initialized = Shell is not initialized
error-start_process = Failed to start the process.
error-file_not_found = file '{ $file }' not found.
error-not_connected_db = Not connected to a database.
error-not_connected_account = Not connected to an account.
error-not_connected_account_or_db =
    Not connected to an account or database.
    Use 'connect <connection-string>' to connect to an account first.
error-not_allowed_in_container = Not allowed inside a container.
error-not_allowed_in_db = Not allowed inside a database.
error-not_inside_container = Not inside a container.
error-not_inside_database = Not inside a database.
error-database_not_found = Database '{ $database }' not found.
    Use 'ls' to see available databases.
error-container_not_found = Container '{ $container }' not found in database '{ $database }'.
    Use 'ls --database { $database }' to see available containers.
error-no_input_data = No input data.
error-argument_parse_redirect = Invalid redirection
error-param_parse = Invalid value for '{ $name }' valid values: { $values }
error-too_many_arguments = Too many arguments ({$expected} expected)
error-unknown_option = Unknown option '{ $option }'
error-missing_value = Option value expected for '{ $option }'
error-missing_required_argument = Missing required argument '{ $arg }'
error-invalid_output_format = Output format '{ $format }' is invalid.
error-request_timeout = The request timed out while communicating with Azure Cosmos DB. Check your network connection and try again. Run with --verbose to show full diagnostics.
error-invalid_bucket_value = Throughput bucket value '{ $bucket }' is invalid. Valid range is 0-5.
error-variable_not_set = Variable '{ $name }' is not set.
error-mutually-exclusive-options = Options '-c' and '-k' cannot be used together.
error-shell-not-initialized = Shell is not initialized
error-unable_to_read_container = Unable to read container.
error-arm-context-required = Database and container resource operations require Azure Resource Manager context. Reconnect with Entra ID and provide --subscription and --resource-group, or use --connect-subscription and --connect-resource-group at startup. Alternatively, use an identity that can discover the Cosmos DB account through ARM.
error-arm-context-incomplete = Provide subscription and resource group together to use an explicit Azure Resource Manager account context. The account name is inferred from the endpoint.
error-arm-context-ambiguous = Multiple Cosmos DB Azure Resource Manager accounts match the connected endpoint. Reconnect and provide subscription and resource group explicitly.
error-arm-context-endpoint-mismatch = The Azure Resource Manager account endpoint '{ $armEndpoint }' does not match the connected Cosmos DB endpoint '{ $dataPlaneEndpoint }'. Reconnect with the subscription and resource group that own the connected account.

help-usage = Usage: { $command }
help-usage-heading = Usage
help-arg = <ARG>
help-arguments = Arguments:
help-arguments-heading = Arguments
help-optional = (Optional)
help-options = Options:
help-options-heading = Options
help-description-not-found = description not found.
help-commands = Commands:
help-examples = Examples:
help-examples-heading = Examples
help-aliases = Aliases:

command-help-description = Shows help information for commands
command-help-description-command = The specific command to get help for
command-help-description-details = Show detailed help information for each command
command-help-description-plain = Disable styling and colors for script or limited terminals

command-rmdb-description = Removes database
command-rmdb-description-name = The database to remove.
command-rmdb-description-force = Force to remove the database without confirmation.
command-rmdb-error-not_allowed_in_container = { error-not_allowed_in_container }
command-rmdb-error-database_not_found = Database { $db } not found.
command-rmdb-deleted_db = Deleted database { $db }.
command-rmdb-confirm_db_deletion = Are you sure you want to delete this database

command-rmcon-description = Removes container
command-rmcon-description-name = The database or container to remove.
command-rmcon-description-force = Force to remove the container without confirmation.
command-rmcon-description-database = The database containing the container to remove
command-rmcon-deleted_container = Deleted container { $container }
command-rmcon-error-container_not_found = Container { $container } not found.
command-rmcon-confirm_container_deletion = Are you sure you want to delete this container

command-rm-description = Removes item from container
command-rm-description-pattern = The pattern for the items to remove.
command-rm-description-force = Force to remove items without confirmation.
command-rm-description-database = The database containing the items to remove
command-rm-description-container = The container containing the items to remove
command-rm-description-key = The property name to match the pattern against (defaults to partition key)
command-rm-deleted_items = Deleted { $count } { $count ->
    [one] item
    *[other] items
}.
command-rm-error-no_filter = Filter is missing.
command-rm-warning-missing-partition-key = Warning: Cannot delete item with id '{ $id }' - missing partition key '{ $partitionKey }'
command-rm-no-matches = No items matched the pattern '{ $pattern }' for key '{ $key }'

command-query-description = Executes a query and returns matching results
command-query-description-query = The query to execute
command-query-description-max = Maximum number of items returned when querying items. Use 0 or a negative value to disable the limit.
command-query-description-metrics = Show query metrics (possible values: Display (default), File (output to data json/csv))
command-query-description-bucket = The throughput bucket to use for the query
command-query-description-format = Output format (json, table, csv)
command-query-description-database = The database to query against
command-query-description-container = The container to query against
command-query-fetched = Fetched { $count } documents.
command-query-request_charge = Request Charge: { $charge } RUs
command-query-document_header = Document
command-query-count_header = Count
command-query-size_header = Size
command-query-retrieved = Retrieved
command-query-output = Output
command-query-time_label = Timings
command-query-document_load = Document load
command-query-query_preparation = Query preparation
command-query-vm_execution = VM execution
command-query-document_write = Document write
command-query-runtime_execution = Runtime execution
command-query-total = Total
command-query-index_lookup_time = Index Lookup Time
command-query-index_hit_ratio = Index Hit Ratio
command-query-index_metrics = Index Utilization Metric
command-query-index_spec = Index Spec
command-query-index_score = Index Impact Score
command-query-index_metric-utilized_single = Utilized Single Indexes
command-query-index_metric-potential_single = Potential Single Indexes
command-query-index_metric-utilized_composite = Utilized Composite Indexes
command-query-index_metric-potential_composite = Potential Composite Indexes
command-query-error-empty_query = Query text cannot be empty. Example: query "SELECT * FROM c".
command-query-error-request_failed = Query request failed with status code { $statusCode } ({ $status }).
command-query-error-no_content_stream = Query response did not contain a content stream.
command-query-error-empty_content = Query response content is empty.

command-print-description = Print a id/partition key.
command-print-description-id = The ID of the item to print
command-print-description-key = The Partition Key of the item to print
command-print-description-format = Output format (json, table)
command-print-description-database = The database containing the item to print
command-print-description-container = The container containing the item to print
command-print-error-item_not_found = Item with id '{ $id }' not found using partition key '{ $partitionKey }'. Verify the partition key value is correct.
command-print-error-request_failed = Request failed for item '{ $id }'. (HTTP { $status })
command-print-error-reading_item = Error reading item: { $message }

command-mkitem-description = Creates items in container
command-mkitem-description-data = JSON data for the item to create
command-mkitem-description-database = The database where items should be created
command-mkitem-description-container = The container where items should be created
command-mkitem-description-force = Create or replace items (upsert behavior)
command-mkitem-created-success = Successfully created item (RU charge: { $charge })
command-mkitem-created-multiple = Successfully created { $count } { $count ->
    [one] item
    *[other] items
} (RU charge: { $charge })
command-mkitem-created-partial = Created { $success } { $success ->
    [one] item
    *[other] items
}, { $failed } failed (RU charge: { $charge })
command-mkitem-upserted-created = Successfully created item (RU charge: { $charge })
command-mkitem-upserted-replaced = Successfully replaced item (RU charge: { $charge })
command-mkitem-upserted-multiple = Upserted items: { $created } created, { $replaced } replaced (RU charge: { $charge })
command-mkitem-upserted-partial = Upserted items: { $created } created, { $replaced } replaced, { $failed } failed (RU charge: { $charge })
command-mkitem-upserted-all-failed = Failed to upsert all { $count } items
command-mkitem-created-all-failed = Failed to create all { $count } items
command-mkitem-error-creation-failed = Failed to create item: { $status } - { $message }
command-mkitem-error-status-returned = Item creation returned: { $status }
command-mkitem-error-array_failed = Failed to write { $failed } of { $total } items.

command-replace-description = Replaces existing items in a container
command-replace-description-data = JSON data for the item to replace
command-replace-description-database = The database containing the items to replace
command-replace-description-container = The container containing the items to replace
command-replace-description-etag = Optional ETag for optimistic concurrency control
command-replace-success-single = Successfully replaced item (RU charge: { $charge })
command-replace-success-multiple = Successfully replaced { $count } { $count ->
    [one] item
    *[other] items
} (RU charge: { $charge })
command-replace-success-partial = Replaced { $success } { $success ->
    [one] item
    *[other] items
}, { $failed } failed (RU charge: { $charge })
command-replace-all-failed = Failed to replace all { $count } items
command-replace-error-invalid_item = Each item must be a JSON object.
command-replace-error-missing_id = Each item must include a non-empty 'id' property.
command-replace-error-missing_partition_key = Each item must include partition key property '{ $path }'.
command-replace-error-status-returned = Item replacement returned: { $status }
command-replace-error-replace-failed = Failed to replace item: { $status } - { $message }
command-replace-error-not_found = Item '{ $id }' not found.
command-replace-error-etag_mismatch = Item '{ $id }' was modified since it was last read (ETag mismatch).
command-replace-error-etag_array_not_supported = The --etag option can only be used when replacing a single item.
command-replace-error-array_failed = Failed to replace { $failed } of { $total } items.

command-patch-description = Applies a single patch operation to an item
command-patch-description-id = The ID of the item to patch
command-patch-description-op = Patch operation: set, add, replace, remove, or incr
command-patch-description-pk = The partition key of the item to patch
command-patch-description-path = JSON path to the field to patch (must start with '/')
command-patch-description-value = Value for the operation (omit for 'remove')
command-patch-description-database = The database containing the item to patch
command-patch-description-container = The container containing the item to patch
command-patch-description-etag = Optional ETag for optimistic concurrency control
command-patch-success = Successfully patched item (RU charge: { $charge })
command-patch-error-missing_id = Item ID is required.
command-patch-error-missing_pk = Partition key is required.
command-patch-error-invalid_pk_json = Partition key must be a JSON scalar value or a JSON array of values for hierarchical partition keys.
command-patch-error-missing_op = Patch operation is required. Supported: set, add, replace, remove, incr.
command-patch-error-missing_path = Patch path is required.
command-patch-error-invalid_path = Patch path must start with '/'.
command-patch-error-missing_value_for_op = Patch operation '{ $op }' requires a value.
command-patch-error-unexpected_value_for_remove = Patch operation 'remove' does not take a value.
command-patch-error-increment_number = Increment patch operation requires a numeric value.
command-patch-error-unsupported_op = Unsupported patch operation '{ $op }'. Usage: patch <op> <id> <pk> <path> [value]. Supported operations: set, add, replace, remove, incr.
command-patch-error-status-returned = Patch operation returned: { $status }
command-patch-error-failed = Failed to patch item: { $status } - { $message }
command-patch-error-not_found = Item '{ $id }' not found.
command-patch-error-etag_mismatch = Item '{ $id }' was modified since it was last read (ETag mismatch).

command-export-description = Exports items from a container to a JSON Lines, JSON array, or CSV file.
command-export-description-file = Destination file path.
command-export-description-database = The database to read from.
command-export-description-container = The container to read from.
command-export-description-query = SELECT query whose results are exported (default: SELECT * FROM c).
command-export-description-max = Maximum number of items to export. 0 means no limit.
command-export-description-format = Output format: jsonl (default), array, or csv.
command-export-description-force = Overwrite the destination file if it already exists.
command-export-success = Exported { $count } { $count ->
    [one] item
    *[other] items
} to { $file } (RU charge: { $charge })
command-export-error-missing_file = A destination file path is required.
command-export-error-file_exists = File '{ $file }' already exists. Use --force to overwrite.
command-export-error-query_failed = Export query failed: { $status } - { $message }

command-import-description = Imports items into a container from a JSON Lines, JSON array, or CSV file.
command-import-description-file = Source file path.
command-import-description-database = The database to write to.
command-import-description-container = The container to write to.
command-import-description-mode = Write mode: insert (default) or upsert.
command-import-description-format = Input format: auto (default), jsonl, array, or csv.
command-import-description-partition-key = For CSV import, the partition key path. Nested paths (e.g. /address/city) place the matching column under that path.
command-import-description-continue-on-error = Continue importing after individual item write failures. Parse or validation errors (invalid JSON, non-object rows, CSV partition-key conflicts) still abort the import.
command-import-description-dry-run = Parse the file without writing any items.
command-import-success = Imported { $count } { $count ->
    [one] item
    *[other] items
} (RU charge: { $charge })
command-import-success-partial = Imported { $success } { $success ->
    [one] item
    *[other] items
}, { $failed } failed (RU charge: { $charge })
command-import-all-failed = Failed to import all { $count } items
command-import-dry-run-success = Dry run: { $count } valid { $count ->
    [one] item
    *[other] items
}
command-import-error-missing_file = A source file path is required.
command-import-error-file_not_found = File '{ $file }' was not found.
command-import-error-blank_line = Line { $line } is blank.
command-import-error-not_object = Line { $line } is not a JSON object.
command-import-error-invalid_line_json = Line { $line } is not valid JSON: { $message }
command-import-error-item_status = Line { $line }: item returned status { $status }.
command-import-error-item_failed = Line { $line }: { $status } - { $message }
command-import-error-some_failed = Failed to import { $failed } of { $total } items.
command-import-error-csv_pk_conflict = CSV column '{ $column }' conflicts with the partition key path '{ $path }': the column holds a scalar value but the path requires it to be a nested object. Rename the column or choose a different partition key path.


command-mkdb-description = Creates new database
command-mkdb-description-name = The database name to create
command-mkdb-description-scale = Container scale (manual or auto)
command-mkdb-description-ru = Container Max RU/s (default: 1000)
command-mkdb-database_created = Created database { $db }
command-mkdb-error-only_auto_or_manual_allowed = Only manual or autoscale are allowed. Not both.

command-mkcon-description = Creates a new container in the current database.
command-mkcon-description-name = The container to create.
command-mkcon-description-partition_key = The partition key path(s) for the container. Use a single path (e.g. /categoryId) or comma-separated paths for hierarchical partition keys (e.g. /tenantId,/userId or /tenantId,/userId,/sessionId).
command-mkcon-description-unique_key = The unique keys for the container to create.
command-mkcon-description-scale = Container scale (manual or auto)
command-mkcon-description-ru = Database Max RU/s (default: 1000)
command-mkcon-description-database = The database where the container should be created
command-mkcon-CreatedContainer = Created container { $container }
command-mkcon-error_partition_key_empty = Partition key path cannot be empty. Provide a path that starts with '/', for example: mkcon name /pk.
command-mkcon-error_partition_key_slash = Partition key path must start with a forward slash (/), for example: mkcon name /pk.
command-mkcon-error_invalid_index_policy = Invalid indexing policy JSON. Please provide a valid Cosmos DB indexing policy.
command-mkcon-description-index_policy = The indexing policy as a JSON string. Follows the Cosmos DB indexing policy schema.

command-indexpolicy-description = Reads or updates the indexing policy of a container.
command-indexpolicy-description-policy = The indexing policy as a JSON string. If omitted, the current policy is displayed.
command-indexpolicy-description-database = The database containing the container
command-indexpolicy-description-container = The container to read/update the indexing policy for
command-indexpolicy-updated = Indexing policy updated successfully.
command-indexpolicy-error_invalid_policy = Invalid indexing policy JSON. Please provide a valid Cosmos DB indexing policy.
command-indexpolicy-error_no_policy = The container has no indexing policy configured.

command-index-description = Manages the indexing policy of a container via show, add, remove, and set subcommands.
command-index-description-subcommand = The action to perform: show, add, remove, or set.
command-index-description-paths = The indexing paths to add or remove, or a full indexing policy JSON document for set.
command-index-description-mode = The indexing mode to set (consistent or none).
command-index-description-automatic = Whether automatic indexing is enabled (true or false).
command-index-description-database = The database containing the container
command-index-description-container = The container to read/update the indexing policy for
command-index-updated = Indexing policy updated successfully.
command-index-error-missing_subcommand = Missing subcommand. Use one of: show, add, remove, set.
command-index-error-invalid_subcommand = Unknown subcommand '{ $subcommand }'. Use one of: show, add, remove, set.
command-index-error-missing_paths = No paths provided. Specify at least one path, for example: index add /address/*.
command-index-error-missing_set_args = Nothing to set. Provide --mode, --automatic, or a full indexing policy JSON document.
command-index-error-invalid_automatic = Invalid value for --automatic. Use true or false.
command-index-error-invalid_mode = Invalid value for --mode. Use consistent or none.
command-index-error-show_no_args = 'index show' does not take any arguments. Use 'index show' to display the current policy.
command-index-error-invalid_policy = Invalid indexing policy JSON. Please provide a valid Cosmos DB indexing policy.
command-index-error-no_policy = The container has no indexing policy configured.

command-throughput-description = Views or changes the provisioned throughput (RU/s) of a database or container via show, set, manual, and autoscale subcommands.
command-throughput-description-subcommand = The action to perform: show, set, manual, or autoscale.
command-throughput-description-ru = The throughput in RU/s to provision (manual RU/s for set/manual, maximum RU/s for autoscale).
command-throughput-description-database = The database to target, or that contains the target container.
command-throughput-description-container = The container to read/update the throughput for.
command-throughput-description-yes = Skip the confirmation prompt before applying a throughput change.
command-throughput-updated = Throughput updated successfully.
command-throughput-confirm_summary = About to set { $mode } throughput to { $ru } RU/s on '{ $resource }'. This may affect your bill.
command-throughput-confirm = Apply this throughput change
command-throughput-cancelled = Throughput change cancelled.
command-throughput-label-scope = Scope
command-throughput-label-resource = Resource
command-throughput-label-mode = Mode
command-throughput-label-throughput = Throughput (RU/s)
command-throughput-label-max = Max throughput (RU/s)
command-throughput-label-min = Min throughput (RU/s)
command-throughput-scope-database = Database
command-throughput-scope-container = Container
command-throughput-mode-autoscale = Autoscale
command-throughput-mode-manual = Manual
command-throughput-mode-none = Not configured
command-throughput-error-missing_subcommand = Missing subcommand. Use one of: show, set, manual, autoscale.
command-throughput-error-invalid_subcommand = Unknown subcommand '{ $subcommand }'. Use one of: show, set, manual, autoscale.
command-throughput-error-missing_ru = No throughput value provided. Specify the RU/s, for example: throughput set 4000.
command-throughput-error-invalid_ru = Invalid throughput value '{ $ru }'. Provide a positive number of RU/s.
command-throughput-error-manual_min = Manual throughput must be at least { $min } RU/s. '{ $ru }' is too low.
command-throughput-error-manual_increment = Manual throughput must be a multiple of { $increment } RU/s. '{ $ru }' is not.
command-throughput-error-autoscale_min = Autoscale maximum throughput must be at least { $min } RU/s. '{ $ru }' is too low.
command-throughput-error-autoscale_increment = Autoscale maximum throughput must be a multiple of { $increment } RU/s. '{ $ru }' is not.
command-throughput-error-show_no_args = 'throughput show' does not take any arguments. Use 'throughput show' to display the current throughput.
command-throughput-error-not_configured = Resource '{ $resource }' has no provisioned throughput to change. It may be serverless or use shared database throughput.
command-throughput-error-rbac =
  You do not have permission to change throughput on the selected account.

  Required action: '{ $permission }'
  Principal id: '{ $id }'

  Learn more: https://aka.ms/cosmos-native-rbac

command-throughput-error-mode_switch_unsupported =
  Switching '{ $resource }' to { $mode } throughput is not supported on this connection.

  The Cosmos data-plane SDK can only change the value within the current mode. To switch between manual and autoscale, connect with an Azure AD (token) credential, or use the Azure portal, Azure CLI, or PowerShell.
command-sproc-description = Manages stored procedures on a container via list, show, exists, create, exec, edit, and delete subcommands.
command-sproc-description-subcommand = The action to perform: list, show, exists, create, exec, edit, or delete.
command-sproc-description-name = The stored procedure id.
command-sproc-description-value = The JavaScript file to read for create, or the JSON array of arguments for exec.
command-sproc-description-partition-key = The partition key used to target a partition when executing a stored procedure.
command-sproc-description-force = Replace the stored procedure if it already exists.
command-sproc-description-database = The database containing the container.
command-sproc-description-container = The container that owns the stored procedures.
command-sproc-created = Created stored procedure '{ $name }' (RU charge: { $charge }).
command-sproc-replaced = Replaced stored procedure '{ $name }' (RU charge: { $charge }).
command-sproc-deleted = Deleted stored procedure '{ $name }' (RU charge: { $charge }).
command-sproc-executed = Executed stored procedure '{ $name }' (RU charge: { $charge }).
command-sproc-edit-launching = Editing stored procedure '{ $name }' with { $editor }.
command-sproc-edit-unchanged = Stored procedure '{ $name }' was not changed.
command-sproc-edit-exit-nonzero = Editor '{ $editor }' exited with status { $code }.
command-sproc-edit-wait = The editor returned immediately. Finish editing, save the file, then press Enter to continue...
command-sproc-create-preview = Stored procedure '{ $name }':
command-sproc-create-confirm = Create this stored procedure
command-sproc-create-discarded = Discarded stored procedure '{ $name }'.
command-sproc-list-empty = No stored procedures found.
command-sproc-list-title = Stored procedures
command-sproc-list-column-id = Id
command-sproc-list-column-modified = Last Modified
command-sproc-list-column-size = Size (chars)
command-sproc-exists-yes = Stored procedure '{ $name }' exists.
command-sproc-exists-no = Stored procedure '{ $name }' does not exist.
command-sproc-error-missing_subcommand = Missing subcommand. Use one of: list, show, exists, create, exec, edit, delete.
command-sproc-error-invalid_subcommand = Unknown subcommand '{ $subcommand }'. Use one of: list, show, exists, create, exec, edit, delete.
command-sproc-error-missing_name = Missing stored procedure name. Specify the id, for example: sproc show myProc.
command-sproc-error-missing_file = No source provided. Specify a JavaScript file or pipe the body in, for example: sproc create myProc ./myProc.js.
command-sproc-error-file_not_found = File not found: '{ $file }'.
command-sproc-error-already_exists = Stored procedure '{ $name }' already exists. Use --force to replace it.
command-sproc-error-not_found = Stored procedure '{ $name }' was not found.
command-sproc-error-invalid_params = Invalid parameters. Provide a JSON array of arguments, for example: '["a", 1, true]'.
command-sproc-error-invalid_pk = Invalid partition key. Provide a JSON scalar, or a JSON array for a hierarchical partition key.
command-sproc-error-missing_partition_key = A partition key is required to execute a stored procedure. Use --partition-key.
command-sproc-error-not_interactive = 'sproc edit' needs an interactive terminal and cannot run from a script or piped input.
command-sproc-error-no_editor = No editor found. Set $VISUAL or $EDITOR to your preferred editor.

command-udf-description = Manages user-defined functions on a container via list, show, exists, create, edit, and delete subcommands.
command-udf-description-subcommand = The action to perform: list, show, exists, create, edit, or delete.
command-udf-description-name = The user-defined function id.
command-udf-description-value = The JavaScript file to read for create.
command-udf-description-force = Replace the user-defined function if it already exists.
command-udf-description-database = The database containing the container.
command-udf-description-container = The container that owns the user-defined functions.
command-udf-created = Created user-defined function '{ $name }' (RU charge: { $charge }).
command-udf-replaced = Replaced user-defined function '{ $name }' (RU charge: { $charge }).
command-udf-deleted = Deleted user-defined function '{ $name }' (RU charge: { $charge }).
command-udf-edit-launching = Editing user-defined function '{ $name }' with { $editor }.
command-udf-edit-unchanged = User-defined function '{ $name }' was not changed.
command-udf-edit-exit-nonzero = Editor '{ $editor }' exited with status { $code }.
command-udf-edit-wait = The editor returned immediately. Finish editing, save the file, then press Enter to continue...
command-udf-create-preview = User-defined function '{ $name }':
command-udf-create-confirm = Create this user-defined function
command-udf-create-discarded = Discarded user-defined function '{ $name }'.
command-udf-list-empty = No user-defined functions found.
command-udf-list-title = User-defined functions
command-udf-list-column-id = Id
command-udf-list-column-size = Size (chars)
command-udf-exists-yes = User-defined function '{ $name }' exists.
command-udf-exists-no = User-defined function '{ $name }' does not exist.
command-udf-error-missing_subcommand = Missing subcommand. Use one of: list, show, exists, create, edit, delete.
command-udf-error-invalid_subcommand = Unknown subcommand '{ $subcommand }'. Use one of: list, show, exists, create, edit, delete.
command-udf-error-missing_name = Missing user-defined function name. Specify the id, for example: udf show myFunc.
command-udf-error-missing_file = No source provided. Specify a JavaScript file or pipe the body in, for example: udf create myFunc ./myFunc.js.
command-udf-error-file_not_found = File not found: '{ $file }'.
command-udf-error-already_exists = User-defined function '{ $name }' already exists. Use --force to replace it.
command-udf-error-not_found = User-defined function '{ $name }' was not found.
command-udf-error-not_interactive = 'udf edit' needs an interactive terminal and cannot run from a script or piped input.
command-udf-error-no_editor = No editor found. Set $VISUAL or $EDITOR to your preferred editor.

command-trigger-description = Manages triggers on a container via list, show, exists, create, edit, and delete subcommands.
command-trigger-description-subcommand = The action to perform: list, show, exists, create, edit, or delete.
command-trigger-description-name = The trigger id.
command-trigger-description-value = The JavaScript file to read for create.
command-trigger-description-type = The trigger type for create: pre or post.
command-trigger-description-operation = The operation the trigger fires on: all, create, replace, delete, or update. Defaults to all.
command-trigger-description-force = Replace the trigger if it already exists.
command-trigger-description-database = The database containing the container.
command-trigger-description-container = The container that owns the triggers.
command-trigger-created = Created trigger '{ $name }' (RU charge: { $charge }).
command-trigger-replaced = Replaced trigger '{ $name }' (RU charge: { $charge }).
command-trigger-deleted = Deleted trigger '{ $name }' (RU charge: { $charge }).
command-trigger-edit-launching = Editing trigger '{ $name }' with { $editor }.
command-trigger-edit-unchanged = Trigger '{ $name }' was not changed.
command-trigger-edit-exit-nonzero = Editor '{ $editor }' exited with status { $code }.
command-trigger-edit-wait = The editor returned immediately. Finish editing, save the file, then press Enter to continue...
command-trigger-create-preview = Trigger '{ $name }':
command-trigger-create-confirm = Create this trigger
command-trigger-create-discarded = Discarded trigger '{ $name }'.
command-trigger-list-empty = No triggers found.
command-trigger-list-title = Triggers
command-trigger-list-column-id = Id
command-trigger-list-column-type = Type
command-trigger-list-column-operation = Operation
command-trigger-list-column-size = Size (chars)
command-trigger-exists-yes = Trigger '{ $name }' exists.
command-trigger-exists-no = Trigger '{ $name }' does not exist.
command-trigger-error-missing_subcommand = Missing subcommand. Use one of: list, show, exists, create, edit, delete.
command-trigger-error-invalid_subcommand = Unknown subcommand '{ $subcommand }'. Use one of: list, show, exists, create, edit, delete.
command-trigger-error-missing_name = Missing trigger name. Specify the id, for example: trigger show myTrigger.
command-trigger-error-missing_file = No source provided. Specify a JavaScript file or pipe the body in, for example: trigger create myTrigger ./myTrigger.js --type pre.
command-trigger-error-file_not_found = File not found: '{ $file }'.
command-trigger-error-already_exists = Trigger '{ $name }' already exists. Use --force to replace it.
command-trigger-error-not_found = Trigger '{ $name }' was not found.
command-trigger-error-missing_type = A trigger type is required. Use --type pre or --type post.
command-trigger-error-invalid_type = Invalid trigger type '{ $type }'. Use pre or post.
command-trigger-error-invalid_operation = Invalid trigger operation '{ $operation }'. Use all, create, replace, delete, or update.
command-trigger-error-not_interactive = 'trigger edit' needs an interactive terminal and cannot run from a script or piped input.
command-trigger-error-no_editor = No editor found. Set $VISUAL or $EDITOR to your preferred editor.

command-ls-description = List resources in the current context.
command-ls-description-filter = The filter pattern.
command-ls-description-max = Maximum number of items returned when listing container items. Defaults to 100 if omitted. Use 0 or a negative value for no limit.
command-ls-description-format = { command-query-description-format }
command-ls-description-recursive = List items recursively
command-ls-description-database = The database to list from
command-ls-description-container = The container to list items from
command-ls-description-key = The property to match against (default: container partition key property)
command-ls-found_items =
    { $count ->
        [0] no items found in container { $container }.
        [one] found { $display } item in container { $container }.
       *[other] found { $display } items in container { $container }.
    }
command-ls-found_databases =
    { $count ->
        [0] no databases found.
        [one] found { $display } database.
       *[other] found { $display } databases.
    }
command-ls-found_containers =
    { $count ->
        [0] no containers found in database { $database }.
        [one] found { $display } container in database { $database }.
       *[other] found { $display } containers in database { $database }.
    }
command-ls-empty_databases_hint = No databases were returned. If you expected some, the connected identity may lack account-level read access, or you may be connected to a different account. Run 'connect' to verify the target account.
command-ls-empty_containers_hint = No containers were returned for database { $database }. If you expected some, the connected identity may lack read access to this database, or you may be targeting the wrong account.
command-ls-error-request_failed = List request failed with status code { $statusCode } ({ $status }).
command-ls-error-no_content_stream = The list request completed, but Cosmos DB returned no response body. This is not an empty-container result; retry the command and use --verbose if it keeps happening.
command-ls-error-empty_content = The list request completed, but Cosmos DB returned an empty response body. This is not an empty-container result; retry the command and use --verbose if it keeps happening.
command-results-limit_reached =
    { $count ->
        [one] Results limited to { $count } item. Use --max to change the limit or --max 0 for no limit.
       *[other] Results limited to { $count } items. Use --max to change the limit or --max 0 for no limit.
    }

command-watch-description = Tail the change feed of a container, streaming new and modified items as they arrive.
command-watch-description-from-beginning = Start from the beginning of the change feed instead of from now.
command-watch-description-partition-key = Scope the change feed to a single partition key value.
command-watch-description-max = Stop after receiving this many changes. Omit, or use 0 or a negative value, to follow until Ctrl+C.
command-watch-description-interval = Seconds to wait between change feed polls once caught up. Defaults to 1; values below 0.1 are clamped.
command-watch-description-format = { command-query-description-format }
command-watch-description-database = The database containing the container to watch.
command-watch-description-container = The container to watch.
command-watch-started = Watching changes in { $container }. Press Ctrl+C to stop.
command-watch-stopped = Stopped watching after { $count } changes.
command-watch-error-request_failed = Change feed request failed with status code { $statusCode } ({ $status }).
command-watch-error-invalid_interval = Interval value '{ $interval }' is invalid. Specify a finite number of seconds.

command-jq-description = Commandline JSON processor
command-jq-description-args = Arguments for the jq command
command-filter-description = Filter and transform piped JSON with the native filter expression language
command-filter-description-expression = Filter expression to evaluate against the piped JSON input
command-filter-error-no_expression = Filter expression is missing.
command-filter-error-no_input = The filter command requires piped JSON input.
command-filter-error-invalid_input = The filter command can only process JSON input.
command-filter-error-trailing_tokens = Unexpected '{ $token }' after the filter expression. Check for stray characters or unbalanced quotes.
command-filter-error-not_json = The filter requires JSON input to evaluate '{ $context }'.
command-filter-error-argument_count = The '{ $name }' filter expects { $expected } argument(s).
command-filter-error-unknown_builtin = Unknown filter builtin '{ $name }'.
command-filter-error-length_type = length supports arrays, objects, strings, and null, not { $type }.
command-filter-error-keys_type = keys requires an object input, not { $type }.
command-filter-error-map_type = map requires an array input, not { $type }.
command-filter-error-select_type = select requires an array input, not { $type }.
command-filter-error-sort_by_type = sort_by requires an array input, not { $type }.
command-filter-error-property_type = Cannot read property '{ $name }' from { $type }.
command-filter-error-index_type = Cannot index { $type } with [{ $index }].
command-filter-error-iterate_type = Cannot iterate over { $type }.
command-filter-error-evaluation = Failed to evaluate the filter expression: { $message }
command-ftab-description = Render piped JSON as a table
command-ftab-description-fields = Comma-separated field names to include in the table (Optional)
command-ftab-description-take = Limit the number of rendered rows (Optional)
command-ftab-description-sort = Sort rows by a field before rendering. Use field or field:asc|desc (Optional)
command-ftab-description-colorize = Colorize terminal cells using field:value:style rules separated by ';' (Optional)
command-ftab-description-format = Output format: default, markdown, or html (Optional)

command-cls-description = Clears the console screen.

command-exit-description = Exits cosmos db shell.

command-disconnect-description = Disconnect the current connection.
command-disconnect-success = Disconnected from '{ $endpoint }'
command-disconnect-not_connected = Not connected to any account.
command-delete-description = Delete items/container or databases.
command-delete-description-item = The object to delete: item, container or database.
command-delete-description-pattern = The items/container/database to delete.
command-delete-description-force = Force to delete without confirmation.
command-delete-description-database = The database for the delete operation
command-delete-description-container = The container for deleting items
command-delete-error-invalid_item_type = You need to specify an item type: 'item', 'database' or 'container' as first parameter.

command-create-description = Creates items/container or databases.
command-create-description-item = The object to create item, container or database.
command-create-description-name = The container or database name to create.
command-create-description-partition_key = { command-mkcon-description-partition_key }
command-create-description-unique_key = { command-mkcon-description-unique_key }
command-create-description-scale = { command-mkcon-description-scale }
command-create-description-ru = Database or Container Max RU/s (default: 1000)
command-create-description-data = JSON data for the item to create
command-create-description-database = The database for the create operation
command-create-description-container = The container for creating items
command-create-description-index_policy = { command-mkcon-description-index_policy }
command-create-description-force = { command-mkitem-description-force }
command-create-error-container_name_required = Create container requires a container name.
command-create-error-partition_key_required = Create container requires a partition key path that starts with '/', for example: create container name /pk. Learn more: https://github.com/Azure/CosmosDBShell/blob/main/docs/commands.md#create
command-create-error-database_name_required = Create database requires a database name.
command-create-error-invalid_item_type = { command-delete-error-invalid_item_type }
command-create-error-force_only_for_items = The --force/--upsert option is only valid for `create item`.

command-connect-description = Connect command.
command-connect-description-connectionString = The account connection string can be a plain url with browser access token or a full connection string with AccountEndpoint and AccountKey values.
command-connect-description-hint = Avoids the account prompt and pre-populates the username of the account to login.
command-connect-description-key = Account key for authentication
command-connect-description-endpoint = Account endpoint URL
command-connect-description-mode = Connection mode: 'direct' (default) or 'gateway'
command-connect-description-tenant = The Entra ID tenant ID to authenticate against.
command-connect-description-authority-host = The authority host URL (The default is https://login.microsoftonline.com/).
command-connect-description-managed-identity = The client ID of a user-assigned managed identity to authenticate with.
command-connect-description-subscription = Azure subscription ID for ARM database and container operations. Must be paired with --resource-group.
command-connect-description-resource-group = Azure resource group name for ARM database and container operations. Must be paired with --subscription.
command-connect-error-no_endpoint = An account endpoint or connection string must be specified.
command-connect-connected = Connected to account '{ $account }'
command-connect-emulator-detected = Emulator endpoint detected, using well-known account key and gateway mode.
command-connect-switching = Disconnecting from '{ $endpoint }'...
command-connect-not_connected = Not connected to any Cosmos DB account.
command-connect-not_connected-usage-header = Use 'connect <endpoint>' to authenticate. Common forms:
command-connect-not_connected-usage-footer = Run 'help connect' for the full list of options.
command-connect-info-title = Connection Information
command-connect-info-account = Account
command-connect-info-arm-account = ARM Account
command-connect-info-endpoint = Endpoint
command-connect-info-mode = Connection Mode
command-connect-info-read-regions = Read Regions
command-connect-info-write-regions = Write Regions
command-connect-info-location = Current Location
command-connect-info-credential = Credential
command-pwd-description = Shows the current shell location.
command-pwd-not-connected = not connected
command-connect-rbac-error =
  You need the 'Data Contributor' RBAC role permission to enable all
  Azure Databases Extension features for the selected account.

  Principal id: '{ $id }'

  It should be enough to run:
  az cosmosdb sql role assignment create --account-name $accountName --resource-group $resourceGroupName --scope "/" --principal-id "{ $id }" --role-definition-id "00000000-0000-0000-0000-000000000002"

  Learn more: https://aka.ms/cosmos-native-rbac

command-cd-description = Changes to container or database
command-cd-description-item = The database or container to select.
command-cd-description-database = The database to navigate to
command-cd-description-container = The container to navigate to
command-cd-description-quiet = Suppress output messages
command-cd-error-failed_change = Failed to change to { $item }: { $msg }
command-cd-changed_to_db = Changed to database { $db }
command-cd-changed_to_connected_state = Changed to connected state.
command-cd-changed_to_container = Changed to container { $container }
command-cd-error-item_empty = Invalid operation - item was empty
command-cd-error-cant_change_inside_container = Can't change from in a container
command-cd-error-database_does_not_exist = Database '{ $db }' not found.
command-cd-error-container_does_not_exist = Container '{ $container }' not found.
command-cd-error-item_and_options = Cannot specify both path and --database/--container options.
command-cd-error-path_too_deep = '{ $path }' goes beyond the /database/container hierarchy. Use 'cd ..' to go up first, or use an absolute path like '/database/container'.

command-cat-description = Displays a file
command-cat-description-path = The path of the file to view.
command-cat-description-encoding = File encoding (utf8, utf16, ascii)

command-edit-description = Opens a file in an external editor and waits for it to close.
command-edit-description-path = The path of the file to edit (created if it does not exist).
command-edit-missing-path = 'edit' requires a file path.
command-edit-not-interactive = 'edit' needs an interactive terminal and cannot run from a script or piped input.
command-edit-create-failed = Could not create '{ $path }': { $message }
command-edit-no-editor = No editor found. Set $VISUAL or $EDITOR to your preferred editor.
command-edit-launching = Editing { $path } with { $editor }
command-edit-launch-failed = Failed to launch editor '{ $editor }' for { $path }: { $message }
command-edit-exit-nonzero = Editor '{ $editor }' exited with status { $code }.
command-edit-saved = Saved { $path }

command-dir-description = Lists files and directories in the local file system.
command-dir-description-filter = File name pattern filter (e.g., *.json, *.cs)
command-dir-description-directory = The directory to list files from
command-dir-description-recursive = List files recursively in subdirectories
command-dir-description-list = Show file names only (simple list format)
command-dir-directory_not_found = Directory '{ $directory }' not found.
command-dir-access_denied = Access denied: { $message }
command-dir-invalid_filter = Invalid filter pattern: { $message }
command-dir-summary = { $fileCount } { $fileCount ->
    [one] file
    *[other] files
}, { $dirCount } { $dirCount ->
    [one] directory
    *[other] directories
}

command-echo-description = Displays messages.
command-echo-description-messages = The messages to display.
command-echo-description-no_newline = Do not append a newline

command-bucket-description = Gets or sets the current throughput bucket.
command-bucket-description-bucket = If specified the number of the bucket to switch to.
command-bucket-currrent = Current throughput bucket: { $bucket }
command-bucket-no_bucket = No throughput bucket is currently set.
command-bucket-reset_bucket = Reset throughput bucket to default.
command-bucket-switched_bucket = Switched to throughput bucket { $bucket }


command-info-description = Shows configuration and usage statistics for the current container, database, or account.
command-info-description-format = Output format (json, table)
command-settings-scale-heading = Scale
command-settings-scale-usage = Based on usage, your container throughput will scale from { $min } RU/s (10% of max RU/s) - { $max } RU/s
command-settings-scale-serverless = Throughput settings are not available for serverless accounts.
command-settings-title = Configuration
command-settings-na = N/A
command-settings-ttl-label = Time to Live
command-settings-Off = Off
command-settings-On = On (no default)
command-settings-ttl-seconds = { $seconds } seconds
command-settings-geospatial-label = Geospatial Configuration
command-settings-geospatial-geography = Geography
command-settings-geospatial-geometry = Geometry
command-settings-partition-key-label = Partition key
command-settings-fulltext-title = Full Text Policy
command-settings-fulltext-default-language-label = Default language
command-settings-fulltext-path-label = Full Text Path
command-settings-fulltext-language-label = Language
command-settings-indexing-title = Indexing Policy
command-settings-indexing-mode-label = Indexing mode
command-settings-indexing-automatic-label = Automatic
command-settings-indexing-paths-label = Paths
command-settings-indexing-paths-value = { $included } included, { $excluded } excluded
command-settings-indexing-composite-label = Composite indexes
command-settings-indexing-spatial-label = Spatial indexes
command-settings-indexing-vector-label = Vector indexes
command-settings-rbac-error =
  You need the '{ $permission }' RBAC role permission for '{ $request }' for the selected account.

  Principal id: '{ $id }'

  Learn more: https://aka.ms/cosmos-native-rbac
command-settings-overview = Overview
command-settings-read_locations = Read Locations
command-settings-write_locations = Write Locations
command-settings-subscription_id = Subscription ID
command-settings-account_id = Account ID
command-settings-uri = URI
command-settings-not-available = N/A
command-info-description-database = Target database name
command-info-description-container = Target container name
command-info-description-partitions = Add the per-physical-partition document distribution (consumes request units)
command-info-description-detailed = Add storage breakdown and top partition keys (performs a full scan and consumes request units)
command-settings-usage-heading = Usage

command-stats-na = N/A
command-stats-database-heading = Database Statistics
command-stats-label-document-count = Document count
command-stats-label-data-size = Data size
command-stats-label-index-size = Index size
command-stats-label-total-size = Total size
command-stats-throughput-heading = Throughput
command-stats-throughput-max = Max RU/s
command-stats-throughput-min = Min RU/s
command-stats-partitions-heading = Physical Partition Distribution
command-stats-partitions-col-partition = Partition
command-stats-partitions-col-count = Document count
command-stats-partitions-col-share = Share
command-stats-partitions-skew = Largest partition holds { $percent }% of documents.
command-stats-partitions-cost-note = Scanning partitions consumes request units.
command-stats-detailed-heading = Top Partition Keys
command-stats-detailed-col-key = Partition key value
command-stats-detailed-col-count = Document count
command-stats-detailed-cost-note = Computing top partition keys performs a full scan and consumes request units.
command-stats-database-label-id = Database
command-stats-database-label-container-count = Containers
command-stats-database-label-total-documents = Total documents
command-stats-database-label-total-size = Total size
command-stats-database-shared-throughput-none = No shared throughput configured.
command-stats-containers-heading = Containers
command-stats-containers-col-name = Container
command-stats-containers-col-count = Documents
command-stats-containers-col-size = Size
command-stats-account-label-database-count = Databases
command-stats-account-label-total-containers = Total containers
command-stats-account-label-total-documents = Total documents
command-stats-account-label-total-size = Total size
command-stats-account-databases-heading = Databases
command-stats-account-databases-col-name = Database
command-stats-account-databases-col-containers = Containers
command-stats-account-databases-col-count = Documents
command-stats-account-databases-col-size = Size
command-stats-account-detailed-cost-note = Aggregating account totals reads every container's quota and consumes request units.

command-version-description = Displays the version of Cosmos DB Shell.
command-version = Cosmos Shell version: { $version }
command-version-mcp = MCP running on port { $mcp_port}
command-version-mcp-off = MCP server is off.
command-version-repo = Report issues at [link={ $url }]{ $url }[/]

help-RequiredWord = Required.
help-ErrorsHeadingText = ERROR(S):
help-UsageHeadingText  = USAGE:
help-UsageSynopsis = { $command } [options] [-c|-k <command>...]
help-CommandTailNote = Everything after -c / -k (or /c, /k) is taken as the command (no quoting needed). App-level options must come before -c / -k.
help-OptionsHeadingText = OPTIONS:
help-NotesHeadingText = NOTES:
help-HelpOptionDescription = Show this help text and exit.
help-VersionOptionDescription = Show product version and exit.
help-OptionGroupWord = Group
help-HelpCommandScreenText = Display this help screen.
help-HelpCommandMoreText = Display more information on a specific command.
help-VersionCommandText = Display version information.
help-cmd = Specifies the command you want to carry out.

help-SentenceMutuallyExclusiveSetErrors =
  { $count ->
    [one] -> Option '{ $option }' is not compatible with: { $incompat }
    *[other] -> Options '{ $option }' are not compatible with: { $incompat }
  }
help-error-BadFormatTokenError = Token '{ $token }' is not recognized.
help-error-MissingValueOptionError = Option '{ $option }' has no value.
help-error-UnknownOptionError = Option '{ $option }' is unknown.
help-error-UnknownArgumentError = Unrecognized argument '{ $argument }'.
help-error-MissingRequiredOptionError1 = A required value not bound to option name is missing.
help-error-MissingRequiredOptionError2 = Required option '{ $option }' is missing.
help-error-BadFormatConversionError1 = A value not bound to option name is defined with a bad format.
help-error-BadFormatConversionError2 = Option '{ $option }' is defined with a bad format.
help-error-SequenceOutOfRangeError1 = A sequence value not bound to option name is defined with few items than required.
help-error-SequenceOutOfRangeError2 = "A sequence option '{ $option }' is defined with fewer or more items than required.
help-error-BadVerbSelectedError = Verb '{ $token }' is not recognized.
help-error-NoVerbSelectedError = No verb selected.
help-error-RepeatedOptionError = Option '{ $option }' is defined multiple times.
help-error-SetValueExceptionError = Error setting value to option '{ $option }': { $message }
help-error-MissingGroupOptionError = At least one option from group '{ $option }"' ({ $req_options }) is required.
help-error-GroupOptionAmbiguityError= Both SetName and Group are not allowed in option: ({ $option })

help-ExecuteAndContinue = Execute the specified command, then keep the shell running.
help-ExecuteAndQuit = Execute the specified command, then exit.
help-ColorSystem = Color system: 0=off, 1=standard, 2=true color (default: 2).
help-ClearHistory = Clears command history and exits.
help-ConnectionString = The endpoint URL or connection string to connect to.
help-ConnectionMode = Connection mode: 'direct' (default) or 'gateway'
help-ConnectTenant = The Entra ID tenant ID to authenticate against at startup.
help-ConnectHint = Login hint for browser authentication at startup.
help-ConnectAuthorityHost = The authority host URL at startup (default: https://login.microsoftonline.com/).
help-ConnectManagedIdentity = The client ID of a user-assigned managed identity at startup.
help-ConnectSubscription = Azure subscription ID for ARM database and container operations at startup.
help-ConnectResourceGroup = Azure resource group name for ARM database and container operations at startup.
help-ConnectVSCodeCredential = Use Visual Studio Code credential for authentication at startup.
help-EnableMcpServer = Enable MCP server for programmatic control of the shell
help-EnableLspServer = Enable Language Server Protocol (LSP) server for editor integration
help-McpPort = Enable MCP HTTP server. Optionally specify a port with --mcp <port>; default is 6128.
help-Verbose = Print full exception details instead of only the message.
help-Theme = Color theme profile to apply at startup. Falls back to the COSMOSDB_SHELL_THEME environment variable.
help-Diagnostics = Write timestamped diagnostic logs to a file. Optionally specify a path with --diagnostics <path>; defaults to a timestamped file in the shell configuration directory.
help-Otel = Enable distributed tracing so requests carry a sampled W3C traceparent. Optionally specify an OTLP endpoint with --otel <endpoint>; falls back to the OTEL_EXPORTER_OTLP_ENDPOINT environment variable.
mcp-error-invalid-port = Error: --mcp port must be greater than 0.
diagnostics-enabled = Writing diagnostic log to { $path }.
diagnostics-error-create = Error: could not create diagnostic log at '{ $path }': { $message }
otel-error-invalid-endpoint = Error: --otel endpoint '{ $endpoint }' is not a valid absolute URI.

warning-unknown-theme = Unknown theme '{ $name }'. Available themes: { $themes }. Falling back to default.

command-theme-description = Inspects and switches the active color theme.
command-theme-description-action = What to do: 'list', 'show', 'use', 'load', 'validate', 'save', 'edit', 'open', or 'reload' (default lists the active theme).
command-theme-description-name = Theme name (for show/use/save/edit) or path to a TOML file (for load/validate/edit).
command-theme-description-path = Optional path. For 'save' the file path to write (default: ~/.cosmosdbshell/themes/<name>.toml). For 'load' and 'validate' the TOML file to read.
command-theme-description-force = Overwrite an existing file when saving, or seed the built-in profile when editing.
command-theme-description-strict = Treat warnings as errors when validating.
command-theme-active = Active theme: { $name }
command-theme-applied = Switched to theme: { $name }
command-theme-sample-heading = Sample of theme '{ $name }':
command-theme-unknown = Unknown theme '{ $name }'. Available themes: { $themes }
command-theme-unknown-action = Unknown 'theme' action '{ $action }'. Available actions: { $actions }
command-theme-use-missing-name = 'theme use' requires a theme name. Run 'theme list' to see available themes.
command-theme-source-builtin = (built-in)
command-theme-source-file = ({ $path })
command-theme-loaded = Loaded theme '{ $name }' from { $path }
command-theme-load-missing-path = 'theme load' requires a path to a TOML file.
command-theme-load-not-found = Theme file not found: { $path }
command-theme-validated = Theme file is valid: '{ $name }' from { $path }
command-theme-validate-missing-path = 'theme validate' requires a path to a TOML file.
command-theme-validate-summary = { $valid } of { $total } theme file(s) valid in { $directory }.
command-theme-validate-no-files = No theme files found in { $directory }.
command-theme-validate-strict-failed = Theme '{ $name }' has { $count } warning(s); strict mode treats them as errors.
command-theme-saved = Saved theme '{ $name }' to { $path }
command-theme-save-missing-name = 'theme save' requires a theme name.
command-theme-save-invalid-name = Invalid theme name '{ $name }'. When --path is omitted, the name must be a simple filename without path separators or invalid characters.
command-theme-save-exists = File already exists: { $path }. Pass --force to overwrite.
command-theme-save-failed = Failed to save theme to { $path }: { $message }
command-theme-save-hint-reload = Run 'theme reload' to register the new file (or 'theme load { $name }' to register and switch to it).
command-theme-reloaded = Reloaded { $count } theme(s) from { $directory }
command-theme-edit-missing-name = 'theme edit' requires a theme name or path. Run 'theme list' to see available themes.
command-theme-edit-builtin-needs-force = '{ $name }' is a built-in theme and has no editable file. Pass --force to copy it to { $path } and edit the copy.
command-theme-edit-seeded = Seeded built-in theme '{ $name }' to { $path }
command-theme-edit-launching = Editing { $path } with { $editor }
command-theme-edit-no-editor = No editor found. Set $VISUAL or $EDITOR to your preferred editor.
command-theme-edit-launch-failed = Failed to launch editor '{ $editor }' for { $path }: { $message }
command-theme-edit-exit-nonzero = Editor '{ $editor }' exited with status { $code }; theme was not reloaded.
command-theme-edit-reload-failed = Theme file '{ $path }' could not be reloaded: { $message }
command-theme-edit-applied = Reloaded and applied theme '{ $name }' from { $path }
command-theme-opened = Opened { $path } in OS file browser.
command-theme-open-failed = Failed to open { $path } in OS file browser: { $message }

theme-file-error-parse = Failed to parse theme file '{ $source }': { $details }
theme-file-error-extends-unknown = Theme file '{ $source }' extends unknown theme '{ $name }'.
theme-file-error-extends-self = Theme file '{ $source }' extends itself ('{ $name }').
theme-file-error-extends-cycle = Theme '{ $name }' (from '{ $source }') is part of an extends cycle.
theme-file-error-extends-too-deep = Theme '{ $name }' (from '{ $source }') has an extends chain that is too deep.
theme-file-error-empty-bracket-cycle = Theme file '{ $source }' has an empty bracket_cycle. At least one color is required.
theme-file-error-invalid-color = Theme file '{ $source }' has invalid value for '{ $key }': '{ $value }'. Allowed colors: { $allowed }
theme-file-error-invalid-color-suggested = Theme file '{ $source }' has invalid value for '{ $key }': '{ $value }'. Allowed colors: { $allowed }. Did you mean '{ $suggestion }'?
theme-file-error-invalid-style = Theme file '{ $source }' has invalid style for '{ $key }': '{ $value }'. Styles may contain modifiers ({ $modifiers }) and at most one color ({ $colors }).
theme-file-error-invalid-style-suggested = Theme file '{ $source }' has invalid style for '{ $key }': '{ $value }'. Styles may contain modifiers ({ $modifiers }) and at most one color ({ $colors }). Did you mean '{ $suggestion }'?
theme-file-warning-bracket-cycle-not-array = Theme file '{ $source }' specifies bracket_cycle but it is not an array; ignoring.
theme-file-warning-bracket-cycle-single = Theme file '{ $source }' has only one bracket_cycle color; nested brackets will not vary by depth.
theme-file-warning-bracket-cycle-duplicates = Theme file '{ $source }' has duplicate bracket_cycle colors; consecutive depths will share a color.
theme-file-warning-unknown-key = Theme file '{ $source }' has unknown key in [{ $section }]: '{ $key }' (ignored).
theme-registry-warning-shadow-builtin = Theme '{ $name }' shadows the built-in profile.
theme-registry-warning-load-failed = Failed to load theme file '{ $path }': { $message }

json_error_property_not_found = Property '{ $property }' not found.
json_error_no_array = JSON data is not an array.
json_error_array_index_out_of_bounds = Array index {$index} is out of bounds. Array length is {$length}.
json_error_parsing_arg =
    Error parsing JSON: {$message}

    Argument must be a valid JSON.
json_error_empty_arraya_brackets = Empty array brackets [] not yet supported.
json_error_invalid_array_index = Invalid array index: [{$index}]
json_error_invalid_char_in_array = Invalid character '{$char}' in array index.
json_error_path_ends_with_escape = Path ends with escape character.
json_error_unclosed_array_bracket = Unclosed array bracket.
json_error_result_evaluation_null = Result evaluation returned null.

expression_error_no_more_tokens = No more tokens
expression_error_expected_open_paren = Expected '('
expression_error_expected_close_paren = Expected ')' after expression
expression_error_expected_close_bracket = Expected ']'
expression_error_expected_array_index = Expected array index
expression_error_invalid_number = Invalid number format: {$value}
expression_error_unexpected_end = Unexpected end of expression
expression_error_unexpected_token = Unexpected token: {$type} '{$value}'
expression_error_unmatched_braces = Unmatched braces in JSON object
expression_error_unmatched_brackets = Unmatched brackets in JSON array
expression_error_invalid_json = Invalid JSON: {$message}
expression_error_expected_comma_or_bracket = Expected ',' or ']' in JSON array (got '{$token}')
expression_error_expected_property_name = Expected property name in JSON object, but got '{ $token }'
expression_error_expected_comma_or_brace = Expected ',' or '{"}"}' in JSON object, but got '{ $token }'

statement_error_expected_after_pipe = Expected statement after pipe '|'
statement_error_expected_if = Expected 'if'
statement_error_expected_after_if = Expected statement after 'if' condition
statement_error_expected_while = Expected 'while'
statement_error_expected_after_while = Expected statement after 'while' condition
statement_error_expected_for = Expected 'for'
statement_error_expected_variable_after_for = Expected variable name after 'for'
statement_error_expected_in = Expected 'in'
statement_error_expected_after_for_collection = Expected statement after 'for' collection
statement_error_expected_do = Expected 'do'
statement_error_expected_after_do = Expected statement after 'do'
statement_error_expected_loop = Expected 'loop'
statement_error_expected_after_loop = Expected statement after 'loop'
statement_error_expected_def = Expected 'def'
statement_error_expected_function_name = Expected function name
statement_error_expected_parameter_name = Expected parameter name
statement_error_expected_close_parenthesis = Expected ')'
statement_error_expected_close_bracket = Expected ']'
statement_error_expected_after_function_def = Expected statement after function definition
statement_error_expected_return = Expected 'return'
statement_error_expected_break = Expected 'break'
statement_error_expected_continue = Expected 'continue'
statement_error_expected_open_brace = Expected '{ "{" }'
statement_error_expected_close_brace = Expected '{ "}" }'
statement_error_unexpected_close_brace = Unexpected '{ "}" }'
statement_error_unexpected_end = Unexpected end of input
statement_error_unexpected_end_parsing_command = Unexpected end of input when parsing command
lexer_error_unterminated_string = Unterminated string literal
statement_error_expected_command_name = Expected command name
statement_error_expected_option_name = Expected option name after '{$prefix}'
statement_error_invalid_option_value = Invalid value for option '{ $option }'
statement_error_expected_redirect_destination = Expected file name after '{$redirect}'
statement_error_invalid_redirect_destination = Invalid destination for '{$redirect}' redirection
statement_error_duplicate_out_redirect = Duplicate output redirection
statement_error_duplicate_err_redirect = Duplicate error redirection

parser-error-prefix = parse error
parser-warning-prefix = parse warning
query-error-prefix = query error
runtime-error-prefix = error
runtime-error-canceled = Canceled.

json_error_empty_array_brackets = Empty array brackets are not allowed.

# Binary operator error messages
expression_error_null_boolean_left = Left operand evaluation returned null for boolean operation
expression_error_null_boolean_right = Right operand evaluation returned null for boolean operation
expression_error_null_json_concat = Operand evaluation returned null for JSON array concatenation
expression_error_null_decimal_add = Operand evaluation returned null for decimal addition
expression_error_null_numeric_add = Operand evaluation returned null for numeric addition
expression_error_null_decimal_subtract = Operand evaluation returned null for decimal subtraction
expression_error_null_numeric_subtract = Operand evaluation returned null for numeric subtraction
expression_error_null_decimal_multiply = Operand evaluation returned null for decimal multiplication
expression_error_null_numeric_multiply = Operand evaluation returned null for numeric multiplication
expression_error_null_decimal_divide = Operand evaluation returned null for decimal division
expression_error_null_numeric_divide = Operand evaluation returned null for numeric division
expression_error_divide_by_zero = Division by zero
expression_error_null_decimal_modulo = Operand evaluation returned null for decimal modulo operation
expression_error_null_numeric_modulo = Operand evaluation returned null for modulo operation
expression_error_modulo_by_zero = Modulo by zero
expression_error_null_decimal_power = Operand evaluation returned null for decimal power operation
expression_error_null_numeric_power = Operand evaluation returned null for power operation
expression_error_negative_exponent_integer = Negative exponents are not supported for integer power operation.
expression_error_null_numeric_equal = Operand evaluation returned null for numeric equality comparison
expression_error_null_decimal_equal = Operand evaluation returned null for decimal equality comparison
expression_error_null_boolean_equal = Operand evaluation returned null for boolean equality comparison
expression_error_null_not_equal = Equality evaluation returned null for not-equal comparison
expression_error_null_decimal_less_than = Operand evaluation returned null for decimal less-than comparison
expression_error_null_numeric_less_than = Operand evaluation returned null for less-than comparison
expression_error_null_decimal_greater_than = Operand evaluation returned null for decimal greater-than comparison
expression_error_null_numeric_greater_than = Operand evaluation returned null for greater-than comparison
expression_error_null_decimal_less_equal = Operand evaluation returned null for decimal less-than-or-equal comparison
expression_error_null_numeric_less_equal = Operand evaluation returned null for less-than-or-equal comparison
expression_error_null_decimal_greater_equal = Operand evaluation returned null for decimal greater-than-or-equal comparison
expression_error_null_numeric_greater_equal = Operand evaluation returned null for greater-than-or-equal comparison
expression_error_null_xor = Operand evaluation returned null for XOR operation
expression_error_unsupported_operator = Binary operator { $operator } is not supported

# MCP Server messages
mcp-error-creating-server = Error creating MCP server: { $message }
mcp-error-server-failed-start = MCP server failed to start: { $message }


# Statements
help-statements = Statements
help-example = Example
help-syntax = Syntax
help-available-commands = Available Commands
help-control-flow-statements = Control Flow Statements
help-list-of-available-commands = List of available commands
help-category-connection = Connection
help-category-connection-styled = Connection
help-category-data-operations = Data Operations
help-category-data-operations-styled = Data Operations
help-category-management = Management
help-category-management-styled = Management
help-category-utilities = Utilities
help-category-utilities-styled = Utilities
help-error-FormatMutuallyExclusiveSetErrors = Conflicting options: { $option }. These cannot be used with { $incompat }.

statement-if-description = Performs conditional processing in shell scripts.
statement-if-syntax = if <expression> <statement> [[[ else <statement> ]]]
statement-if-example =
 if ($age > 18) {"{"}
 {"    "}echo "Adult"
 {"}"} else {"{"}
 {"    "}echo "Minor"
 {"}"}

statement-while-description = Repeats a statement while the given expression evaluates to true.
statement-while-syntax = while <expression> <statement>
statement-while-example =
 while ($count < 10) {"{"}
 {"    "}echo $count
 {"    "}$count = $count + 1
 {"}"}

statement-loop-description = Repeats a statement indefinitely until a break statement is encountered.
statement-loop-syntax = loop <statement>
statement-loop-example =
 loop {"{"}
 {"    "}echo $count
 {"    "}if ($count >= 10) {"{"}
 {"        "}echo "Done"
 {"        "}break
 {"    "}{"}"}
 {"    "}$count = $count + 1
 {"}"}

statement-do-description = Executes a statement once, then repeats it while the expression remains true.
statement-do-syntax = do <statement> while <expression>
statement-do-example =
 do {"{"}
 {"    "}echo $count
 {"    "}$count = $count + 1
 {"}"} while ($count < 10)

statement-for-description = Iterates over each element of a collection, assigning it to a loop variable and executing a statement.
statement-for-syntax = for $<variable> in <expression> <statement>
statement-for-example =
 for $item in [["apple","banana","cherry"]] {"{"}
 {"    "}echo $item
 {"}"}

statement-def-description = Defines a named function with optional parameters that can be invoked later.
statement-def-syntax = def <name> [[[[parameter1 parameter2 ...]]]] <statement>
statement-def-example =
 def greet [[name]] {"{"}
 {"    "}echo "Hello, " $name
 {"}"}

statement-return-description = Exits the current function (or script) optionally returning a value.
statement-return-syntax = return [[[ <expression> ]]]
statement-return-example =
 def add [[a b]] {"{"}
 {"    "}$sum = $a + $b
 {"    "}return $sum
 {"}"}

statement-break-description = Exits the nearest enclosing loop immediately.
statement-break-syntax = break
statement-break-example =
 while ($count < 10) {"{"}
 {"    "}if ($count == 5) {"{"}
 {"        "}echo "Stopping at 5"
 {"        "}break
 {"    "}{"}"}
 {"    "}echo $count
 {"    "}$count = $count + 1
 {"}"}

statement-continue-description = Skips the remainder of the current loop iteration and continues with the next iteration.
statement-continue-syntax = continue
statement-continue-example =
 for $item in [["apple","skip","cherry"]] {"{"}
 {"    "}if ($item == "skip") {"{"}
 {"        "}continue
 {"    "}{"}"}
 {"    "}echo $item
 {"}"}

statement-exec-description = Dynamically evaluates an expression to get a command or script path and executes it with optional arguments.
statement-exec-syntax = exec <expression> [[[ <argument> ... ]]]
statement-exec-example =
 $script = {"{"}path: "myscript.csh", name: "My Script"{"}"}
 exec $script.path arg1 arg2

 for $file in (dir "*.csh") {"{"}
 {"    "}exec $file.path
 {"}"}


hover-statement-title = `{ $keyword }` statement
hover-variable-title = Variable `$`{ $name }
hover-variable-defined = Defined at line { $line }, column { $column }.
hover-variable-references = References: { $count }
hover-command-unknown = Unknown command '{ $name }'.
hover-function-title = Function '{ $name }'
hover-command-restricted-warning = This command is restricted in MCP contexts.
hover-command-usage = **Usage:**
hover-command-arguments = **Arguments:**
hover-command-options = **Options:**
hover-command-examples = **Examples:**
hover-optional = (optional)
hover-aliases = aliases
hover-no-description = (no description)
hover-syntax = **Syntax:**
hover-example = **Example:**
