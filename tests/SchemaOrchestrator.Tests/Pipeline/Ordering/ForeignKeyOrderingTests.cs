using SchemaOrchestrator.Models;
using SchemaOrchestrator.Services;

namespace SchemaOrchestrator.Tests.Pipeline.Ordering;

public class ForeignKeyOrderingTests
{
    [Fact]
    public void Compare_OrdersForeignKeyAroundColumnChange()
    {
        var comparer = new SchemaComparer();

        var current = new DatabaseSchema
        {
            Tables =
            [
                new TableSchema
                {
                    Schema = "dbo",
                    Name = "Parents",
                    Columns =
                    [
                        new ColumnSchema("Id", "int", false, true)
                    ]
                },
                new TableSchema
                {
                    Schema = "dbo",
                    Name = "Children",
                    Columns =
                    [
                        new ColumnSchema("Id", "int", false, true),
                        new ColumnSchema("ParentId", "int", false)
                    ]
                }
            ],
            ForeignKeys =
            [
                new ForeignKeySchema("dbo", "Children", "FK_Children_Parents_ParentId", ["ParentId"], "dbo", "Parents", ["Id"])
            ]
        };

        var desired = new DatabaseSchema
        {
            Tables =
            [
                new TableSchema
                {
                    Schema = "dbo",
                    Name = "Parents",
                    Columns =
                    [
                        new ColumnSchema("Id", "int", false, true)
                    ]
                },
                new TableSchema
                {
                    Schema = "dbo",
                    Name = "Children",
                    Columns =
                    [
                        new ColumnSchema("Id", "int", false, true),
                        new ColumnSchema("ParentId", "bigint", false)
                    ]
                }
            ],
            ForeignKeys =
            [
                new ForeignKeySchema("dbo", "Children", "FK_Children_Parents_ParentId_New", ["ParentId"], "dbo", "Parents", ["Id"])
            ]
        };

        var ops = comparer.Compare(current, desired, DestructiveChangePolicy.Archive);

        Assert.Contains(ops, o => o.Kind == SchemaOperationKind.DropForeignKey);
        Assert.Contains(ops, o => o.Kind == SchemaOperationKind.AlterColumn);

        var dropFkIndex = Array.FindIndex([.. ops], o => o.Kind == SchemaOperationKind.DropForeignKey);
        var alterIndex = Array.FindIndex([.. ops], o => o.Kind == SchemaOperationKind.AlterColumn);
        var createFkIndex = Array.FindIndex([.. ops], o => o.Kind == SchemaOperationKind.CreateForeignKey);

        Assert.True(dropFkIndex >= 0);
        Assert.True(alterIndex >= 0);
        Assert.True(createFkIndex >= 0);
        Assert.True(dropFkIndex < alterIndex);
        Assert.True(alterIndex < createFkIndex);
    }
}
