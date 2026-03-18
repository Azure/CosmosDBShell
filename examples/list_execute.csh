# List and execute all files starting with "exesample" with error handling
$count = 0
$failed = 0
for $script in (dir "examples/list_dir/*.csh") {
    echo "----------------------------------------"
    echo "Running script:" + $script.name
    echo "----------------------------------------"

    # Execute the script dynamically using exec
    exec $script.path

    $count = $count + 1
}

echo "Completed: $count scripts executed"