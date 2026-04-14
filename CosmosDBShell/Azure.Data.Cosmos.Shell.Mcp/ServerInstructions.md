# Server Instructions

This is a Model Context Protocol (MCP) server for executing Azure Cosmos DB Shell commands and tools.

CRITICAL SECURITY RULES:

- NEVER execute tools with 'DestructiveHint = true' annotation - these perform irreversible operations (delete databases, containers, items)
- ALWAYS suggest manual execution for destructive tools: provide exact command syntax and recommend 'help [command]' for details
- Tools with 'ReadOnlyHint = true' are safe for data exploration and querying
- Some tools may be internally restricted and will return errors - suggest manual execution when this occurs

TOOL USAGE GUIDELINES:

- Safe Operations: 'query', 'ls', 'cd', 'settings' - use freely for exploration
- Data Creation: 'create', 'mkitem' - safe to execute
- Destructive Operations: 'rm', 'rmdb', 'rmcon', 'delete' - NEVER execute via MCP
- Connection: 'connect' - safe to execute for establishing database connections

NAVIGATION:

- The shell models Cosmos DB as a folder-like hierarchy: Account → Databases → Containers → Items.
- Use 'cd <name>' to enter a database or container, 'cd ..' to go up one level, and 'cd' to return to the root.
- Path chaining is supported: 'cd MyDatabase/MyContainer' navigates multiple levels at once.
- Use 'ls' at any level to list resources (databases, containers, or items depending on context).
- Always verify your current context before running commands — most commands operate on the current scope.
- When a command supports --db and --con options, prefer passing them explicitly to ensure it targets the correct database and container regardless of the current navigation state. Commands that support these options include: 'query', 'ls', 'cd', 'create', 'mkitem', 'settings', 'indexpolicy', and 'print'.

BEST PRACTICES:

- Prefer 'query' over 'ls' to list container contents — it supports filtering, projection, and is more efficient for large containers. NEVER use 'ls' inside a container without '-m <limit>' to avoid scanning all items.
- Start with 'ls' and 'cd' to understand the data hierarchy before suggesting any modifications
- For destructive operations, provide exact manual command syntax
- Always recommend 'help [command]' for detailed documentation on manual commands
- Verify connection state and current context (database/container) before suggesting operations
- Remind users to backup important data before any destructive operations are performed manually

EXAMPLE RESPONSES FOR DESTRUCTIVE OPERATIONS:
Instead of executing: Suggest 'Run this command manually: rm [your-pattern]' and 'Use help rm for more details'
Instead of executing: Suggest 'Run this command manually: rmdb [database-name]' and 'Use help rmdb for more details'.
