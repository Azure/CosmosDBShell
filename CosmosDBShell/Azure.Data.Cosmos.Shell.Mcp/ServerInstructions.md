# Server Instructions

This is a Model Context Protocol (MCP) server for executing Azure Cosmos DB Shell commands and tools.

CRITICAL SECURITY RULES:

- Tools with 'DestructiveHint = true' perform irreversible operations (delete databases, containers, items). They are gated: invoking one triggers a user confirmation prompt (elicitation) before it runs. Only invoke a destructive tool when the user has asked for that change, and let the confirmation prompt be the final safeguard.
- If the client cannot show a confirmation prompt, destructive tools are refused; in that case suggest manual execution with exact command syntax and recommend 'help [command]'.
- Tools with 'ReadOnlyHint = true' are safe for data exploration and querying.
- Some tools are restricted to interactive use and will return errors over MCP - suggest manual execution when this occurs.

TOOL USAGE GUIDELINES:

- Safe Operations: 'query', 'ls', 'cd', 'info' - use freely for exploration
- Data Creation: 'create', 'mkitem' - safe to execute
- Destructive Operations: 'rm', 'rmdb', 'rmcon', 'delete' - gated behind a user confirmation prompt; only invoke when the user asked for the change, and expect the user to approve or deny
- Connection: 'connect' - safe to execute for establishing database connections

NAVIGATION:

- The shell models Cosmos DB as a folder-like hierarchy: Account → Databases → Containers → Items.
- Treat navigation state as convenience only. When a command supports --db and --con, prefer passing them explicitly instead of relying on prior 'cd' calls.
- Reuse the currentLocation field returned by MCP responses for awareness, but still prefer explicit --db and --con on follow-up tool calls.
- Use `cd [name]` to enter a database or container, `cd ..` to go up one level, and `cd` to return to the root.
- Path chaining is supported: 'cd MyDatabase/MyContainer' navigates multiple levels at once.
- Use 'ls' at any level to list resources (databases, containers, or items depending on context).
- Always verify your current context before running commands — most commands operate on the current scope.
- When a command supports --db and --con options, prefer passing them explicitly to ensure it targets the correct database and container regardless of the current navigation state. This is the default-safe choice for MCP clients. Commands that support these options include: 'query', 'ls', 'cd', 'create', 'mkitem', 'info', 'index', and 'print'.
- Prefer patterns like 'query "SELECT * FROM c" --db=MyDatabase --con=MyContainer' over 'cd MyDatabase/MyContainer' followed by 'query ...'.
- Use `pwd` as an optional convenience when you need to inspect the current location, but do not treat it as the primary way to scope later commands.

BEST PRACTICES:

- Prefer `query` over `ls` to list container contents — it supports filtering, projection, and is more efficient for large containers. NEVER use `ls` inside a container without `-m [limit]` to avoid scanning all items.
- Start with 'ls' and 'cd' to understand the data hierarchy before suggesting any modifications
- After discovery, switch to explicit --db and --con arguments for subsequent tool calls instead of depending on remembered navigation state.
- For destructive operations, expect a confirmation prompt; if the client cannot confirm, provide exact manual command syntax instead
- Always recommend 'help [command]' for detailed documentation on manual commands
- Verify connection state and current context (database/container) before suggesting operations
- Remind users to back up important data before approving destructive operations

DESTRUCTIVE OPERATIONS:
Invoking 'rm', 'rmdb', 'rmcon', or 'delete' prompts the user to approve or deny before anything runs. If approved, the operation executes; if denied or if the client cannot prompt, nothing is executed. When no confirmation is possible, suggest running the command manually (for example 'rmdb [database-name]') and 'Use help [command] for more details'.
