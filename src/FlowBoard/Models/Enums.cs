namespace FlowBoard.Models;

public enum Priority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

public enum AttachmentKind
{
    File,
    Image,
    Voice,
    Link,
}

public enum DueState
{
    None,
    Normal,
    Soon,
    Overdue,
    Done,
}

public enum BoardViewMode
{
    Board,
    Table,
    Calendar,
}

public enum ThemeMode
{
    Dark,
    Light,
    System,
}

public enum BackdropMode
{
    Mica,
    Acrylic,
    Tabbed,
    None,
}

public enum DueFilter
{
    Any,
    Overdue,
    Today,
    ThisWeek,
    NoDate,
    HasDate,
}
