EXEC [__AddStoredProcInfo]
    /* StoredProc_Name         */ 'Articles_GetArticles',
    /* Internal_Indicator      */ 'N',
    /* ReturnType_Name         */ 'DataTable',
    /* DataTableNames_Csv      */ 'Articles_Extended',
    /* OutputPropertyNames_Csv */ NULL,
    /* Description_Text        */ NULL
GO

IF OBJECT_ID('[Articles_GetArticles]') IS NOT NULL
	DROP PROCEDURE [Articles_GetArticles]
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [Articles_GetArticles]
(
    @Series_Slug NVARCHAR(255) = NULL,
    @PublishedStatus_Name VARCHAR(15),
    @RangeStart_Date DATETIME,
    @RangeEnd_Date DATETIME,
    @Author_Slug NVARCHAR(255) = NULL
)
AS
BEGIN


    SELECT * FROM [Articles_Extended]
            WHERE (@Series_Slug IS NULL OR [Series_Slug] = @Series_Slug)
              AND (@Author_Slug IS NULL OR [Author_Slug] = @Author_Slug)
              AND (@PublishedStatus_Name IS NULL OR [PublishedStatus_Name] = @PublishedStatus_Name)
              AND (@PublishedStatus_Name <> 'Published' OR [Published_Date] < GETDATE())
              AND (@RangeStart_Date IS NULL OR [Published_Date] > @RangeStart_Date)
              AND (@RangeEnd_Date IS NULL OR [Published_Date] < @RangeEnd_Date)
         ORDER BY [Published_Date] DESC

END
GO

GRANT EXECUTE ON [Articles_GetArticles] TO [TheDailyWtfUser_Role]
GO
