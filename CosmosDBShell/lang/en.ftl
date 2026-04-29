shell-ready = Cosmos DB shell ready.
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

yes_char = Y
no_char = N

error = Error:
error-connection_failed = Failed to connect to the Cosmos DB account.
error-command-not-found = '{ $command }' is not recognized as an internal or external command, operable program or batch file.
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
command-query-request_charge = Request Charge: [white]{ $charge } RUs[/]
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
command-mkitem-created-success = Successfully created item (RU charge: { $charge })
command-mkitem-created-multiple = Successfully created { $count } { $count ->
    [one] item
    *[other] items
} (RU charge: { $charge })
command-mkitem-created-partial = Created { $success } { $success ->
    [one] item
    *[other] items
}, { $failed } failed (RU charge: { $charge })
command-mkitem-created-all-failed = Failed to create all { $count } items
command-mkitem-error-creation-failed = Failed to create item: { $status } - { $message }
command-mkitem-error-status-returned = Item creation returned: { $status }


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
command-mkcon-error_partition_key_empty = Partition key cannot be empty.
command-mkcon-error_partition_key_slash = Partition key must start with a forward slash (/).
command-mkcon-error_invalid_index_policy = Invalid indexing policy JSON. Please provide a valid Cosmos DB indexing policy.
command-mkcon-description-index_policy = The indexing policy as a JSON string. Follows the Cosmos DB indexing policy schema.

command-indexpolicy-description = Reads or updates the indexing policy of a container.
command-indexpolicy-description-policy = The indexing policy as a JSON string. If omitted, the current policy is displayed.
command-indexpolicy-description-database = The database containing the container
command-indexpolicy-description-container = The container to read/update the indexing policy for
command-indexpolicy-updated = Indexing policy updated successfully.
command-indexpolicy-error_invalid_policy = Invalid indexing policy JSON. Please provide a valid Cosmos DB indexing policy.

command-ls-description = List resources in the current context.
command-ls-description-filter = The filter pattern.
command-ls-description-max = Maximum number of items returned when listing container items. Defaults to 100 if omitted. Use 0 or a negative value for no limit.
command-ls-description-format = { command-query-description-format }
command-ls-description-recursive = List items recursively
command-ls-description-database = The database to list from
command-ls-description-container = The container to list items from
command-ls-description-key = The property to match against (default: container partition key property)
command-ls-container = Container { $container }
command-ls-found_items = found { $count } items.
command-results-limit_reached =
    { $count ->
        [one] Results limited to { $count } item. Use --max to change the limit or --max 0 for no limit.
       *[other] Results limited to { $count } items. Use --max to change the limit or --max 0 for no limit.
    }

command-jq-description = Commandline JSON processor
command-jq-description-args = Arguments for the jq command
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
command-create-error-container_name_required = Create container requires a container name.
command-create-error-partition_key_required = Create container requires a partition key.
command-create-error-database_name_required = Create database requires a database name.
command-create-error-invalid_item_type = { command-delete-error-invalid_item_type }

command-connect-description = Connect command.
command-connect-description-connectionString = The account connection string can be a plain url with browser access token or a full connection string with AccountEndpoint and AccountKey values.
command-connect-description-hint = Avoids the account prompt and pre-populates the username of the account to login.
command-connect-description-key = Account key for authentication
command-connect-description-endpoint = Account endpoint URL
command-connect-description-mode = Connection mode: 'direct' (default) or 'gateway'
command-connect-description-tenant = The Entra ID tenant ID to authenticate against.
command-connect-description-authority-host = The authority host URL (The default is https://login.microsoftonline.com/).
command-connect-description-managed-identity = The client ID of a user-assigned managed identity to authenticate with.
command-connect-error-no_endpoint = An account endpoint or connection string must be specified.
command-connect-connected = Connected to account '{ $account }'
command-connect-emulator-detected = Emulator endpoint detected, using well-known account key and gateway mode.
command-connect-switching = Disconnecting from '{ $endpoint }'...
command-connect-not_connected = Not connected to any Cosmos DB account.
command-connect-info-title = Connection Information
command-connect-info-account = Account
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

command-cat-description = Displays a file
command-cat-description-path = The path of the file to view.
command-cat-description-encoding = File encoding (utf8, utf16, ascii)

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


command-settings-description = Shows various settings for databases and containers.
command-settings-description-format = Output format (json, table)
command-settings-scale-heading = Scale
command-settings-scale-usage = Based on usage, your container throughput will scale from { $min } RU/s (10% of max RU/s) - { $max } RU/s
command-settings-title = Settings
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
command-settings-description-database = Target database name
command-settings-description-container = Target container name

command-version-description = Displays the version of Cosmos DB Shell.
command-version = Cosmos Shell version: { $version }
command-version-mcp = MCP running on port { $mcp_port}
command-version-mcp-off = MCP server is off.
command-version-repo = Report issues at [link={ $url }]{ $url }[/]

help-RequiredWord = Required.
help-ErrorsHeadingText = ERROR(S):
help-UsageHeadingText  = USAGE:
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

help-ExecuteAndContinue = Executes the specified command and keeps the shell running (for example: /k "help").
help-ExecuteAndQuit = Executes the specified command and exits the shell (for example: /c "help").
help-ColorSystem = ColorSystem to use.(0=off, 1=standard, 2=true color)
help-ClearHistory = Clears command history and exits.
help-ConnectionString = The endpoint URL or connection string to connect to.
help-ConnectionMode = Connection mode: 'direct' (default) or 'gateway'
help-ConnectTenant = The Entra ID tenant ID to authenticate against at startup.
help-ConnectHint = Login hint for browser authentication at startup.
help-ConnectAuthorityHost = The authority host URL at startup (default: https://login.microsoftonline.com/).
help-ConnectManagedIdentity = The client ID of a user-assigned managed identity at startup.
help-ConnectVSCodeCredential = Use Visual Studio Code credential for authentication at startup.
help-EnableMcpServer = Enable MCP server for programmatic control of the shell
help-EnableLspServer = Enable Language Server Protocol (LSP) server for editor integration
help-McpPort = Enable MCP HTTP server. Optionally specify a port with --mcp <port>; default is 6128.
help-Verbose = Print full exception details instead of only the message.
mcp-error-invalid-port = Error: --mcp port must be greater than 0.

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
expression_error_expected_close_paren = Expected ')' after expression
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
statement_error_expected_open_brace = Expected '\u007B'
statement_error_expected_close_brace = Expected '\u007D'
statement_error_unexpected_close_brace = Unexpected '\u007D'
statement_error_unexpected_end = Unexpected end of input
statement_error_unexpected_end_parsing_command = Unexpected end of input when parsing command
statement_error_expected_command_name = Expected command name
statement_error_expected_option_name = Expected option name after '{$prefix}'
statement_error_invalid_option_value = Invalid value for option '{ $option }'
statement_error_expected_redirect_destination = Expected file name after '{$redirect}'
statement_error_invalid_redirect_destination = Invalid destination for '{$redirect}' redirection
statement_error_duplicate_out_redirect = Duplicate output redirection
statement_error_duplicate_err_redirect = Duplicate error redirection

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
mcp-error-creating-server = Error creating MCP server: [red]{ $message }[/]
mcp-error-server-failed-start = MCP server failed to start: [red]{ $message }[/]


# Statements
help-statements = Statements
help-example = Example
help-syntax = Syntax
help-available-commands = Available Commands
help-available-commands-styled = [bold white]Available Commands[/]
help-control-flow-statements = Control Flow Statements:
help-control-flow-statements-styled = [bold white]Control Flow Statements[/]
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
