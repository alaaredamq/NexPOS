namespace Cloud.Data;

/// <summary>What the guest's "Send order" does on the public menu. The owner picks one in the menu editor.</summary>
public static class MenuOrderModes
{
    /// <summary>Nothing is sent: the order stays on the guest's phone, to show to staff (and, if shown, send on WhatsApp).</summary>
    public const string Show = "show";
    /// <summary>The order is sent to the owner, who sees it in the menu editor's order list and answers it there.</summary>
    public const string Request = "request";
    /// <summary>The order is sent to the till of the guest's branch, where the cashier loads it into a sale. Needs a linked POS.</summary>
    public const string Pos = "pos";

    public const string Default = Show;
    public static readonly string[] All = [Show, Request, Pos];
    public static bool IsValid(string? s) => s is not null && All.Contains(s);
}

/// <summary>Where an order stands. The guest sees it on their phone; the owner or the cashier moves it on.</summary>
public static class MenuOrderStatus
{
    public const string New = "new";
    public const string Accepted = "accepted";
    public const string Ready = "ready";
    public const string Done = "done";
    public const string Rejected = "rejected";

    public static readonly string[] All = [New, Accepted, Ready, Done, Rejected];
    public static readonly string[] Open = [New, Accepted, Ready];
    public static bool IsValid(string? s) => s is not null && All.Contains(s);
}

/// <summary>
/// An order a guest sent from the public menu. Prices are worked out here from the menu as it is when the order arrives, never taken
/// from the guest's phone. It goes to the owner's order list, or to the till of its branch (<see cref="SiteId"/>, mode <c>pos</c>).
/// </summary>
public class MenuOrder
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    /// <summary>The order number the guest and the staff say out loud (#104): counts up per menu.</summary>
    public int Number { get; set; }
    /// <summary>Random letters in the guest's status link; nobody else can look the order up.</summary>
    public string Token { get; set; } = "";
    /// <summary><see cref="MenuOrderModes.Request"/> (to the owner) or <see cref="MenuOrderModes.Pos"/> (to the till), as set when it was sent.</summary>
    public string Route { get; set; } = MenuOrderModes.Request;
    /// <summary>The branch the guest ordered at; null when the menu has no branch.</summary>
    public Guid? SiteId { get; set; }
    public string? BranchName { get; set; }
    public string? OrderType { get; set; }
    public string GuestName { get; set; } = "";
    public string? Phone { get; set; }
    /// <summary>Table number or delivery address, as the guest typed it.</summary>
    public string? Place { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = MenuOrderStatus.New;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<MenuOrderLine> Lines { get; set; } = [];
}

/// <summary>One dish of an order, with its name and price as they were when it was ordered.</summary>
public class MenuOrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public MenuOrder Order { get; set; } = null!;
    public int? MenuItemId { get; set; }
    /// <summary>The product at the order's branch POS, so the till can ring it up; null for a hand-made dish.</summary>
    public int? PosProductId { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "";
}
