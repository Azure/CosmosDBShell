echo $"Running script $0, connecting to $1"
connect $1
echo "Switch to first database"
ls | cd $.[0]
echo "Switch to first container"
ls | cd $.[0]