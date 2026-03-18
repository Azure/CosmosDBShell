# While loops
$counter = 0
while ($counter < 5) {
    echo "Count: $counter"
    $counter = $counter + 1
}

# For loops
for i in [1, 2, 3, 4, 5] {
    if $i == 5 {
        continue
    }
    if $i == 10 {
        break
    }
    echo $i

    for i in [1, 2, 3, 4, 5] {
        echo "Item: $i"
    }
}