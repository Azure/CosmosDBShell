# If statements
if true {
    echo "Simple if"
}

if 1 == 1 {
    echo "Comparison"
} else {
    echo "Should not print"
}

if $x > 10 {
    echo "Greater"
} elif $x == 10 {
    echo "Equal"
} else {
    echo "Less"
}

# Nested if
if true {
    if false {
        echo "Inner false"
    } else {
        echo "Inner else"
    }
}

# Complex conditions
if ($a > 5 && $b < 10) || $c == 0 {
    echo "Complex condition"
}