using System;
using ProEdit.Ribbon;
using Xunit;

namespace ProEdit.Ribbon.Tests;

public sealed class RibbonModelTests
{
    [Fact]
    public void Builder_SetsTopBarSearch()
    {
        var builder = new RibbonModelBuilder();
        var search = new RibbonTextBox("search", "Search");

        builder.AddTab("home", "Home")
            .AddGroup(new RibbonGroup("grp", "Group", Array.Empty<IRibbonControl>()));
        builder.SetTopBarSearch(search);

        var model = builder.Build();

        Assert.Same(search, model.TopBarSearch);
    }

    [Fact]
    public void RefreshState_UpdatesTopBarSearch()
    {
        var text = "alpha";
        var search = new RibbonTextBox("search", "Search", textEvaluator: () => text);
        var tab = new RibbonTab("home", "Home", Array.Empty<RibbonGroup>());
        var model = new RibbonModel(new[] { tab })
        {
            TopBarSearch = search
        };

        Assert.Equal("alpha", search.Text);

        text = "beta";
        model.RefreshState();

        Assert.Equal("beta", search.Text);
    }
}
