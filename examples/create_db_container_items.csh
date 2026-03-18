# Usage: run this script with: CosmosShell examples/create_db_container_items.run "<connection-string>"
# $0 = script name, $1 = connection string (passed in by the shell framework like the connect.run example)

echo $"Running script '$0' against account '$1'" 
connect '$1'


# Create (or reuse) a database named 'sampledb'
mkdb sampledb
# Enter the database
cd sampledb

# Create (or reuse) a container named 'items' with partition key '/pk'
mkcon items /pk

# Ensure we're in the database (in case context changed) then enter container
cd items

# Loop over a small set of numbers and create one item per iteration
# for $i in [ ... ] { <body> }
for $i in [1,2,3,4,5] {
  echo $"Creating item $i"
  # mkitem reads JSON lines from the piped echo. We wrap each object in an array to avoid
  # the fast-path property enumeration in mkitem and ensure a single document is created.
  echo $"[{\"id\":\"item$i\",\"pk\":\"$i\",\"value\":$i}]" | mkitem
}

# Show all inserted documents
ls