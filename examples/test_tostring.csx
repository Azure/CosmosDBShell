using Azure.Data.Cosmos.Shell.Parser;

var script = @"
if $x > 10 {
    for $i in [1, 2, 3] {
        echo $i
    }
} else {
    while $count < 5 {
        $count = $count + 1
    }
}
";

var parser = new StatementParser(script);
var statements = parser.ParseStatements();

foreach (var stmt in statements)
{
    Console.WriteLine("Statement Type: " + stmt.GetType().Name);
    Console.WriteLine("ToString(): " + stmt.ToString());
    Console.WriteLine();
}
