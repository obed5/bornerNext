-- ---------------------------------------------------------------------------
-- Jeu d'essai SYNTHÉTIQUE pour la reprise de l'historique (Lot 9).
--
-- Ce script crée une base « origine » au schéma du système précédent, peuplée de
-- quelques lignes inventées (aucune donnée personnelle réelle, aucun extrait de
-- production) : il sert à vérifier la migration en atelier et en intégration.
--
-- Usage :  sqlcmd -S ".\SQLEXPRESS" -E -i tools\legacy-fixture.sql
-- Puis  :  BorderNext.Migration migrate --source "…LegacyFixture" --target "…BorderNext"
-- ---------------------------------------------------------------------------

IF DB_ID('LegacyFixture') IS NOT NULL
BEGIN
    ALTER DATABASE LegacyFixture SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE LegacyFixture;
END
GO
CREATE DATABASE LegacyFixture;
GO
USE LegacyFixture;
GO

-- ---- Nomenclatures --------------------------------------------------------

CREATE TABLE lCountries (
    OldID varchar(2) NULL, ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL,
    Name nvarchar(510) NULL, Alpha2 varchar(2) NULL, Alpha3 varchar(3) NULL, SortOrder smallint NOT NULL,
    [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lCountriesCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL, Name nvarchar(200) NULL);

CREATE TABLE lDocumentTypes (
    OldID smallint NULL, ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL,
    SortOrder smallint NOT NULL, [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lDocumentTypesCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL, Description nvarchar(100) NULL);

CREATE TABLE lMovementDirections (
    ID smallint NOT NULL PRIMARY KEY, OldID varchar(1) NULL, ExternalID nvarchar(100) NULL,
    SortOrder tinyint NOT NULL, [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lMovementDirectionsCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL,
    Abbreviation nvarchar(20) NULL, Description nvarchar(800) NULL);

CREATE TABLE lSex (
    ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL, SortOrder smallint NOT NULL,
    [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lSexCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL, Description nvarchar(100) NULL);

CREATE TABLE lVisaTypes (
    ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL, SortOrder smallint NOT NULL,
    [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lVisaTypesCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL, Description nvarchar(100) NULL);

CREATE TABLE lVisaEntry (
    ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL, SortOrder smallint NOT NULL,
    [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lVisaEntryCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL, Description nvarchar(100) NULL);

CREATE TABLE lBorderPoints (
    OldID varchar(3) NULL, ID smallint NOT NULL PRIMARY KEY, ExternalID nvarchar(100) NULL,
    SortOrder tinyint NOT NULL, [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL);
CREATE TABLE lBorderPointsCultures (
    ID smallint NOT NULL, Culture varchar(10) NOT NULL, OwnerID smallint NOT NULL,
    Name nvarchar(100) NULL, RegionalSite nvarchar(100) NULL);
GO

-- ---- Dossier frontalier ---------------------------------------------------

CREATE TABLE Persons (
    ID nvarchar(40) NOT NULL PRIMARY KEY, UpdateUser nvarchar(60) NULL,
    UpdateDate datetime NULL, BorderPointID smallint NULL);

CREATE TABLE Documents (
    ID varchar(20) NOT NULL PRIMARY KEY, DocumentTypeID smallint NOT NULL, DocumentNumber nvarchar(40) NOT NULL,
    DocumentCountryOfIssueID smallint NOT NULL, DocumentIssueDate datetime NULL, DocumentExpireDate datetime NULL,
    Surname nvarchar(80) NOT NULL, GivenName nvarchar(80) NOT NULL, FatherName nvarchar(80) NULL,
    DateOfBirth datetime NULL, SexID smallint NOT NULL, Citizenship smallint NOT NULL,
    UpdateDate datetime NULL, BorderPointID smallint NULL, UpdateUser nvarchar(100) NULL,
    PersonalNumber nvarchar(40) NULL, CreateDate datetime NULL, CreatorUser nvarchar(100) NULL);

CREATE TABLE Photos (
    DocumentID varchar(20) NOT NULL PRIMARY KEY, Photo varbinary(max) NULL, ScannedDocument varbinary(max) NULL);

CREATE TABLE PassportRegistrationData (
    ID varchar(20) NOT NULL PRIMARY KEY, OldID varchar(20) NULL, DocumentID varchar(20) NOT NULL,
    TravelDate datetime NOT NULL, TransportTypeID smallint NULL, TransportCrossBorderID varchar(20) NULL,
    MovementDirectionID smallint NOT NULL, IsDriver bit NOT NULL DEFAULT 0, PersonID nvarchar(40) NOT NULL,
    TravelReasonID smallint NULL, Granted bit NOT NULL DEFAULT 1, DeportationReasonID smallint NULL,
    IsTransit bit NOT NULL DEFAULT 0, HasVisa bit NOT NULL DEFAULT 0, VisaID varchar(20) NULL,
    ChildrenCount int NOT NULL DEFAULT 0, Note nvarchar(500) NULL, ResidentCountryID smallint NULL,
    DestinationCountryID smallint NULL, Occupation nvarchar(100) NULL, AccomodationAddress nvarchar(200) NULL,
    AccomodationTypeID smallint NULL, DurationOfStay smallint NULL, Inspector nvarchar(100) NULL,
    PCNumber nvarchar(20) NULL, RegistrationTime datetime NOT NULL, UpdateDate datetime NULL,
    BorderPointID smallint NULL, CameraPhoto varbinary(max) NULL);

CREATE TABLE Children (
    ID varchar(20) NOT NULL PRIMARY KEY, OldID varchar(20) NULL, PassportRegistrationDataID varchar(20) NOT NULL,
    Surname nvarchar(80) NOT NULL, GivenName nvarchar(80) NOT NULL, DateOfBirth datetime NOT NULL,
    SexID smallint NOT NULL, Citizenship smallint NOT NULL, DocumentTypeID smallint NULL,
    DocumentNumber nvarchar(40) NULL, Attorney nvarchar(800) NULL, Ticket bit NULL,
    UpdateUser nvarchar(100) NULL, UpdateDate datetime NULL, BorderPointID smallint NULL);

CREATE TABLE Visa (
    ID varchar(20) NOT NULL PRIMARY KEY, DocumentID varchar(20) NULL, VisaTypeID smallint NOT NULL,
    VisaEntryID smallint NULL, VisaNumber nvarchar(40) NULL, IssueDate datetime NULL, ExpireDate datetime NULL,
    Duration smallint NULL, ReferenceNumber nvarchar(40) NULL, ReceiptNumber nvarchar(40) NULL,
    Address nvarchar(200) NULL, ContactNumber nvarchar(40) NULL, Employer nvarchar(100) NULL,
    InvitedBy nvarchar(100) NULL, RelationID smallint NULL, UniversityID smallint NULL,
    PermitStatusID smallint NULL, Occupation nvarchar(100) NULL, PeriodTypeID smallint NULL,
    Note nvarchar(500) NULL, VoucherNumber nvarchar(40) NULL, PermitConditionID smallint NULL,
    ProvinceID smallint NULL, Barcode nvarchar(100) NULL, PCNumber nvarchar(20) NULL,
    [Revoke] bit NOT NULL DEFAULT 0, [RevokeDate] datetime NULL, CreateDate datetime NULL,
    CreatorUser nvarchar(100) NULL, UpdateDate datetime NULL, UpdateUser nvarchar(100) NULL,
    BorderPointID smallint NULL);

CREATE TABLE VisaChildren (
    ID varchar(20) NOT NULL PRIMARY KEY, VisaID varchar(20) NOT NULL, Surname nvarchar(80) NULL,
    GivenName nvarchar(80) NULL, OtherNames nvarchar(80) NULL, DateOfBirth datetime NULL,
    SexID smallint NULL, Citizenship smallint NULL, DocumentNumber nvarchar(40) NULL);

CREATE TABLE Permit (
    ID varchar(20) NOT NULL PRIMARY KEY, DocumentID varchar(20) NOT NULL, VisaID varchar(20) NULL,
    PermitTypeID smallint NULL, Number nvarchar(40) NULL, IssueDate datetime NULL, ExpireDate datetime NULL,
    StatusID smallint NULL, MaritalStatusID smallint NULL, OccupationID smallint NULL,
    OccupationOther nvarchar(100) NULL, QualificationID smallint NULL, CourseOfStudyID smallint NULL,
    DocumentIssuingAuthority nvarchar(100) NULL, BloodTypeID smallint NULL, UpdateDate datetime NULL,
    UpdateUser nvarchar(100) NULL, ProfessionID smallint NULL, CreateDate datetime NULL,
    CreateUser nvarchar(100) NULL, Note nvarchar(500) NULL, PCNumber nvarchar(20) NULL,
    CameraPhoto varbinary(max) NULL);

CREATE TABLE PermitMedicalHistory (
    ID varchar(20) NOT NULL PRIMARY KEY, PermitID varchar(20) NOT NULL, PermitDependantID varchar(20) NULL,
    CurrentIllness nvarchar(200) NULL, CurrentMedication nvarchar(200) NULL, MalariaSuffered bit NULL,
    YellowFeverSuffered bit NULL, TBSuffered bit NULL, MeaslesSuffered bit NULL, CholeraSuffered bit NULL,
    TyphoidSuffered bit NULL, JaundiceSuffered bit NULL, ChronicCoughSuffered bit NULL,
    OtherSuffered nvarchar(200) NULL, YellowFeverVaccinated bit NULL, MMRVaccinated bit NULL,
    DiphtheriaVaccinated bit NULL, PolioVaccinated bit NULL, UpdateDate datetime NULL, UpdateUser nvarchar(100) NULL,
    HealthCentreVisited bit NULL, ChronicDesease bit NULL, InfectiousDesease bit NULL, isPublicHealthCentre bit NULL);

CREATE TABLE PersonsStopList (
    ID varchar(20) NOT NULL PRIMARY KEY, OldID varchar(20) NULL, Surname nvarchar(80) NULL,
    GivenName nvarchar(80) NULL, FathersName nvarchar(80) NULL, SurnameAlt nvarchar(80) NULL,
    GivenNameAlt nvarchar(80) NULL, FathersNameAlt nvarchar(80) NULL, DateOfBirth datetime NULL,
    SexID smallint NULL, Citizenship smallint NULL, WhoSetUp nvarchar(100) NULL, SetUpDate datetime NULL,
    CancelDate datetime NULL, StopListSourceID smallint NULL, SourceDocument nvarchar(100) NULL,
    Info nvarchar(1000) NULL, Photo varbinary(max) NULL, DocumentTypeID smallint NULL,
    DocumentNumber nvarchar(40) NULL, PersonalNumber nvarchar(40) NULL, ActionRequired nvarchar(200) NULL,
    MatchInStopListActionID smallint NULL, UpdateDate datetime NULL, LastUpdateUser nvarchar(100) NULL,
    [Timestamp] datetime NULL, CancelStopListSourceID smallint NULL, CancelSourceDocument nvarchar(100) NULL,
    IsAuthorized bit NULL);

CREATE TABLE DocumentsStopList (
    ID varchar(20) NOT NULL PRIMARY KEY, OldID varchar(20) NULL, DocumentTypeID smallint NULL,
    DocumentNumber nvarchar(40) NULL, PersonalNumber nvarchar(40) NULL, CountryID smallint NULL,
    Surname nvarchar(80) NULL, GivenName nvarchar(80) NULL, FathersName nvarchar(80) NULL,
    DateOfBirth datetime NULL, SexID smallint NULL, SetUpDate datetime NULL, CancelDate datetime NULL,
    StopListSourceID smallint NULL, SourceDocument nvarchar(100) NULL, Info nvarchar(1000) NULL,
    ActionRequired nvarchar(200) NULL, MatchInStopListActionID smallint NULL, UpdateDate datetime NULL,
    LastUpdateUser nvarchar(100) NULL, [Timestamp] datetime NULL, CancelStopListSourceID smallint NULL,
    CancelSourceDocument nvarchar(100) NULL);

CREATE TABLE DoubtablePersons (
    PassportRegistrationID varchar(20) NOT NULL PRIMARY KEY, PhotoJustScanned varbinary(max) NULL,
    Supervisor nvarchar(100) NULL, SupervisorRegistrationTime datetime NULL, SupervisorPCNumber nvarchar(20) NULL,
    Status smallint NULL, PersonStopID varchar(20) NULL, DocumentStopID varchar(20) NULL,
    InspectorsNote nvarchar(500) NULL, SupervisorsNote nvarchar(500) NULL, PerformedActionID smallint NULL,
    CheckingReason smallint NULL, RefusalOfficerName nvarchar(100) NULL, RefusalOfficerRank nvarchar(50) NULL,
    RefusalDocNumber nvarchar(40) NULL, RefusalCountryID smallint NULL, RefusalJustificationID smallint NULL,
    RefusalJustificationOther nvarchar(200) NULL, RefusalCreateDate datetime NULL);
GO

-- ---- Lignes inventées -----------------------------------------------------

INSERT INTO lCountries (OldID, ID, ExternalID, Name, Alpha2, Alpha3, SortOrder, [Revoke]) VALUES
 ('CD', 1, 'COD', 'CONGO, LA REPUBLIQUE DEMOCRATIQUE DU', 'CD', 'COD', 10, 0),
 ('FR', 2, 'FRA', 'FRANCE', 'FR', 'FRA', 20, 0),
 ('BE', 3, 'BEL', 'BELGIQUE', 'BE', 'BEL', 30, 0);

INSERT INTO lCountriesCultures (ID, Culture, OwnerID, Name) VALUES
 (1, 'fr', 1, N'CONGO, LA REPUBLIQUE DEMOCRATIQUE DU'),
 (1, 'en', 1, N'CONGO, THE DEMOCRATIC REPUBLIC OF THE'),
 (2, 'fr', 2, N'FRANCE'), (2, 'en', 2, N'FRANCE');

INSERT INTO lDocumentTypes (OldID, ID, ExternalID, SortOrder, [Revoke]) VALUES
 (1, 1, 'OP', 10, 0),   -- passeport ordinaire
 (2, 2, 'PD', 20, 0),   -- passeport diplomatique
 (3, 3, 'TST', 30, 0);  -- document d'essai, absent du référentiel cible

INSERT INTO lDocumentTypesCultures (ID, Culture, OwnerID, Description) VALUES
 (1, 'fr', 1, N'Passeport ordinaire'), (1, 'en', 1, N'Ordinary passport'),
 (2, 'fr', 2, N'Passeport diplomatique'), (2, 'en', 2, N'Diplomatic passport'),
 (3, 'fr', 3, N'Document d''essai'), (3, 'en', 3, N'Fixture document');

INSERT INTO lMovementDirections (ID, OldID, ExternalID, SortOrder, [Revoke]) VALUES
 (1, 'E', 'Entry', 10, 0), (2, 'S', 'Exit', 20, 0);
INSERT INTO lMovementDirectionsCultures (ID, Culture, OwnerID, Abbreviation, Description) VALUES
 (1, 'fr', 1, N'Entrée', N'Entrée'), (1, 'en', 1, N'Entry', N'Entry'),
 (2, 'fr', 2, N'Sortie', N'Sortie'), (2, 'en', 2, N'Exit', N'Exit');

INSERT INTO lSex (ID, ExternalID, SortOrder, [Revoke]) VALUES (1, 'F', 10, 0), (2, 'M', 20, 0);
INSERT INTO lSexCultures (ID, Culture, OwnerID, Description) VALUES
 (1, 'fr', 1, N'Féminin'), (1, 'en', 1, N'Female'), (2, 'fr', 2, N'Masculin'), (2, 'en', 2, N'Male');

INSERT INTO lBorderPoints (OldID, ID, ExternalID, SortOrder, [Revoke]) VALUES
 ('FIH', 2, 'FIH', 10, 0), ('DGM', 1, 'HQ', 20, 0);
INSERT INTO lBorderPointsCultures (ID, Culture, OwnerID, Name, RegionalSite) VALUES
 (2, 'fr', 2, N'Kinshasa-N''djili', N'Aéroport'), (2, 'en', 2, N'Kinshasa-N''djili', N'Airport'),
 (1, 'fr', 1, N'DGM', N'Siège'), (1, 'en', 1, N'DGM', N'Headquarters');

INSERT INTO Persons (ID, UpdateUser, UpdateDate, BorderPointID) VALUES
 ('P000000001', 'fixture', '2026-09-01T08:00:00', 2),
 ('P000000002', 'fixture', '2026-09-02T09:30:00', 2),
 ('P000000003', 'fixture', '2026-09-03T10:15:00', 2);

INSERT INTO Documents (ID, DocumentTypeID, DocumentNumber, DocumentCountryOfIssueID, DocumentIssueDate,
                       DocumentExpireDate, Surname, GivenName, FatherName, DateOfBirth, SexID, Citizenship,
                       UpdateDate, BorderPointID, UpdateUser, PersonalNumber, CreateDate, CreatorUser) VALUES
 ('D000000001', 1, 'OP000001', 1, '2021-01-10T00:00:00', '2031-01-09T00:00:00', N'MARTIN', N'CLAIRE', NULL, '1990-04-12T00:00:00', 1, 1,
  '2026-09-01T08:00:00', 2, 'fixture', NULL, '2021-01-10T00:00:00', 'fixture'),
 ('D000000002', 2, 'PD000002', 2, '2022-05-20T00:00:00', '2032-05-19T00:00:00', N'DUBOIS', N'PATRICK', N'ANTOINE', '1985-11-03T00:00:00', 2, 2,
  '2026-09-02T09:30:00', 2, 'fixture', 'PERS00002', '2022-05-20T00:00:00', 'fixture'),
 ('D000000003', 3, 'TST000003', 3, '2023-03-01T00:00:00', '2033-02-28T00:00:00', N'ESSOR', N'LINE', NULL, '1995-07-21T00:00:00', 1, 3,
  '2026-09-03T10:15:00', 2, 'fixture', NULL, '2023-03-01T00:00:00', 'fixture');

-- BMP 1x1 minimal valide (en-tête + une ligne de pixels) : la reprise transporte une image lisible.
INSERT INTO Photos (DocumentID, Photo, ScannedDocument) VALUES
 ('D000000001',
  0x424D3A0000000000000036000000280000000100000001000000010018000000000004000000000000000000000000000000FFFFFF00,
  0x424D3A0000000000000036000000280000000100000001000000010018000000000004000000000000000000000000000000FFFFFF00);

INSERT INTO lVisaTypes (ID, ExternalID, SortOrder, [Revoke]) VALUES (1, 'Tourist Visa', 10, 0), (2, 'TST', 20, 0);
INSERT INTO lVisaTypesCultures (ID, Culture, OwnerID, Description) VALUES
 (1, 'fr', 1, N'Visa de tourisme'), (1, 'en', 1, N'Tourist visa'),
 (2, 'fr', 2, N'Visa d''essai'), (2, 'en', 2, N'Fixture visa');

INSERT INTO lVisaEntry (ID, ExternalID, SortOrder, [Revoke]) VALUES (1, 'Single-entry', 10, 0), (2, 'Multiple-entry', 20, 0);
INSERT INTO lVisaEntryCultures (ID, Culture, OwnerID, Description) VALUES
 (1, 'fr', 1, N'Entrée unique'), (1, 'en', 1, N'Single entry'),
 (2, 'fr', 2, N'Entrées multiples'), (2, 'en', 2, N'Multiple entries');

INSERT INTO Children (ID, PassportRegistrationDataID, Surname, GivenName, DateOfBirth, SexID, Citizenship,
                      DocumentTypeID, DocumentNumber, BorderPointID) VALUES
 ('C000000001', 'R000000002', N'DUBOIS', N'LEA', '2018-06-15T00:00:00', 1, 2, 1, 'OP000777', 2);

INSERT INTO Visa (ID, DocumentID, VisaTypeID, VisaEntryID, VisaNumber, IssueDate, ExpireDate, Duration,
                     PCNumber, [Revoke], CreateDate, CreatorUser, UpdateDate, UpdateUser, BorderPointID) VALUES
 ('V000000001', 'D000000002', 1, 1, 'VISA-2026-000001', '2026-08-01T00:00:00', '2027-02-01T00:00:00', 180, 'A02',
  0, '2026-08-01T00:00:00', 'fixture', '2026-08-01T00:00:00', 'fixture', 2);

INSERT INTO VisaChildren (ID, VisaID, Surname, GivenName, DateOfBirth, SexID, Citizenship) VALUES
 ('VC0000001', 'V000000001', N'DUBOIS', N'LEA', '2018-06-15T00:00:00', 1, 2);

INSERT INTO Permit (ID, DocumentID, VisaID, PermitTypeID, Number, IssueDate, ExpireDate, StatusID,
                    OccupationOther, DocumentIssuingAuthority, UpdateDate, UpdateUser, CreateDate, CreateUser,
                    Note, PCNumber) VALUES
 ('PM00000001', 'D000000002', 'V000000001', NULL, 'PERM-2026-0001', '2026-08-15T00:00:00', '2027-08-14T00:00:00',
  NULL, N'Commerçant indépendant', N'Direction générale de migration', '2026-08-15T00:00:00', 'fixture',
  '2026-08-15T00:00:00', 'fixture', N'Permis d''essai', 'A02');

INSERT INTO PermitMedicalHistory (ID, PermitID, CurrentIllness, CurrentMedication, MalariaSuffered,
                                  YellowFeverSuffered, TBSuffered, MeaslesSuffered, CholeraSuffered,
                                  TyphoidSuffered, JaundiceSuffered, ChronicCoughSuffered, OtherSuffered,
                                  YellowFeverVaccinated, MMRVaccinated, DiphtheriaVaccinated, PolioVaccinated,
                                  UpdateDate, UpdateUser, HealthCentreVisited, ChronicDesease, InfectiousDesease,
                                  isPublicHealthCentre) VALUES
 ('MH00000001', 'PM00000001', N'Aucune', NULL, 0, 0, 0, 1, 0, 0, 0, 0, NULL, 1, 0, 1, 1,
  '2026-08-15T00:00:00', 'fixture', 1, 0, 0, 1);

INSERT INTO PassportRegistrationData (ID, DocumentID, TravelDate, MovementDirectionID, IsDriver, PersonID,
                                      Granted, IsTransit, HasVisa, VisaID, ChildrenCount, Note, ResidentCountryID,
                                      DestinationCountryID, DurationOfStay, Inspector, PCNumber,
                                      RegistrationTime, UpdateDate, BorderPointID) VALUES
 ('R000000001', 'D000000001', '2026-09-01T07:45:00', 1, 0, 'P000000001', 1, 0, 0, NULL, 0, NULL, 1, 2, 30, 'fixture', 'A01',
  '2026-09-01T08:00:00', '2026-09-01T08:00:00', 2),
 -- Mouvement rattaché à un visa migré après lui : exerce le rattachement différé.
 ('R000000002', 'D000000002', '2026-09-02T09:20:00', 1, 1, 'P000000002', 1, 0, 1, 'V000000001', 0, N'Passage de service', 2, 1, 10, 'fixture', 'A02',
  '2026-09-02T09:30:00', '2026-09-02T09:30:00', 2),
 ('R000000003', 'D000000002', '2026-09-04T18:05:00', 2, 1, 'P000000002', 1, 1, 1, NULL, 0, NULL, 2, 3, 5, 'fixture', 'A02',
  '2026-09-04T18:10:00', '2026-09-04T18:10:00', 2),
 -- Refus d'entrée (mouvement non accordé).
 ('R000000004', 'D000000003', '2026-09-03T10:00:00', 1, 0, 'P000000003', 0, 0, 0, NULL, 0, N'Refus d''essai', 3, 1, 0, 'fixture', 'A01',
  '2026-09-03T10:15:00', '2026-09-03T10:15:00', 2),
 -- Document absent de l'origine : mouvement rejeté et compté (contrôle du rapport).
 ('R000000005', 'D000000004', '2026-09-05T12:00:00', 1, 0, 'P000000001', 1, 0, 0, NULL, 0, NULL, 1, 2, 1, 'fixture', 'A01',
  '2026-09-05T12:05:00', '2026-09-05T12:05:00', 2);

INSERT INTO DocumentsStopList (ID, DocumentTypeID, DocumentNumber, CountryID, Surname, GivenName, SetUpDate,
                               StopListSourceID, Info, ActionRequired, UpdateDate, LastUpdateUser) VALUES
 ('DS00000001', 2, 'PD000002', 2, N'DUBOIS', N'PATRICK', '2026-08-20T00:00:00', NULL,
  N'Document signalé (jeu d''essai)', N'Retenir le document', '2026-08-20T00:00:00', 'fixture');

INSERT INTO PersonsStopList (ID, Surname, GivenName, DateOfBirth, SexID, Citizenship, WhoSetUp, SetUpDate,
                             StopListSourceID, ActionRequired, DocumentTypeID, DocumentNumber, UpdateDate,
                             LastUpdateUser) VALUES
 ('S000000001', N'ESSOR', N'LINE', '1995-07-21T00:00:00', 1, 3, 'fixture', '2026-08-01T00:00:00', NULL,
  N'Signaler au superviseur', 3, 'TST000003', '2026-08-01T00:00:00', 'fixture');

INSERT INTO DoubtablePersons (PassportRegistrationID, Supervisor, SupervisorRegistrationTime, SupervisorPCNumber,
                              Status, PersonStopID, InspectorsNote, CheckingReason) VALUES
 ('R000000004', N'Superviseur D''essai', '2026-09-03T10:30:00', 'A09', 2, 'S000000001',
  N'Document d''essai présenté', 3);
GO

PRINT 'Base d''origine synthétique LegacyFixture créée.';
GO
