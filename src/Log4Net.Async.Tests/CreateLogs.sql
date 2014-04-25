CREATE DATABASE [Logs]
GO

USE [Logs]
GO

/****** Object:  Table [dbo].[Log4Net]    Script Date: 02/14/2012 21:44:03 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET ANSI_PADDING ON
GO

CREATE TABLE [dbo].[Log4Net](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Date] [datetime] NOT NULL,
	[Thread] [varchar](255) NOT NULL,
	[Level] [varchar](50) NOT NULL,
	[Logger] [varchar](255) NOT NULL,
	[Message] [varchar](8000) NOT NULL,
	[Exception] [varchar](8000) NULL,
	[Application] [varchar](100) NULL,
	[Hostname] [varchar](50) NULL
) ON [PRIMARY]

GO

SET ANSI_PADDING OFF
GO
