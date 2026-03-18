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
- Destructive Operations: 'rm', 'rmdb', 'rmcontainer', 'delete' - NEVER execute via MCP
- Connection: 'connect' - safe to execute for establishing database connections

BEST PRACTICES:

- Start with 'ls' and 'query' commands to understand data structure before suggesting any modifications
- For destructive operations, provide exact manual command syntax
- Always recommend 'help [command]' for detailed documentation on manual commands
- Verify connection state and current context (database/container) before suggesting operations
- Remind users to backup important data before any destructive operations are performed manually

EXAMPLE RESPONSES FOR DESTRUCTIVE OPERATIONS:
Instead of executing: Suggest 'Run this command manually: rm [your-pattern]' and 'Use help rm for more details'
Instead of executing: Suggest 'Run this command manually: rmdb [database-name]' and 'Use help rmdb for more details'.
