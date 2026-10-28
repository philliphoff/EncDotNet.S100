namespace EncDotNet.S100.Nmea.Tests;

public sealed class NmeaSentenceTests
{
    [Fact]
    public void A_sentence_splits_into_talker_formatter_and_fields()
    {
        var sentence = NmeaSentence.Parse("$IIMWV,083.0,R,13.8,N,A*0C");

        Assert.Equal('$', sentence.StartCharacter);
        Assert.Equal("II", sentence.Talker);
        Assert.Equal("MWV", sentence.Formatter);
        Assert.Equal(["083.0", "R", "13.8", "N", "A"], sentence.Fields);
        Assert.True(sentence.HasChecksum);
        Assert.False(sentence.IsProprietary);
        Assert.Equal(83.0, sentence.Number(0));
        Assert.Equal('R', sentence.Letter(1));
        Assert.Equal(string.Empty, sentence.Field(9));
        Assert.Null(sentence.Number(9));
    }

    [Theory]
    [InlineData("$IIMTW,12.4,C*14\r\n")]
    [InlineData("  $IIMTW,12.4,C*14  ")]
    [InlineData("$IIMTW,12.4,C*14")]
    public void Line_endings_and_surrounding_spaces_are_ignored(string line)
    {
        Assert.True(NmeaSentence.TryParse(line, out var sentence, out var error));
        Assert.Equal(NmeaParseError.None, error);
        Assert.Equal("$IIMTW,12.4,C*14", sentence.Raw);
    }

    [Fact]
    public void Lower_case_checksum_digits_are_accepted()
    {
        Assert.True(NmeaSentence.TryParse("$IIMWD,200.0,T,214.5,M,14.0,N,7.2,M*74", out _, out _));
        Assert.True(NmeaSentence.TryParse("$IIMWV,083.0,R,13.8,N,A*0c", out _, out _));
    }

    [Theory]
    [InlineData("$IIMTW,12.4,C*15")] // one bit off
    [InlineData("$IIMTW,12.5,C*14")] // a corrupted digit
    [InlineData("$IIMWV,083.0,R,13.8,N,A*0D")]
    public void A_bad_checksum_is_rejected(string line)
    {
        Assert.False(NmeaSentence.TryParse(line, out var sentence, out var error));
        Assert.Null(sentence);
        Assert.Equal(NmeaParseError.ChecksumMismatch, error);
    }

    [Theory]
    [InlineData("$IIMTW,12.4,C*1", NmeaParseError.MalformedChecksum)]
    [InlineData("$IIMTW,12.4,C*1G", NmeaParseError.MalformedChecksum)]
    [InlineData("$IIMTW,12.4,C*140", NmeaParseError.MalformedChecksum)]
    [InlineData("$IIMTW,12.4,C*", NmeaParseError.MalformedChecksum)]
    public void A_malformed_checksum_is_rejected(string line, NmeaParseError expected)
    {
        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("$IIMWD,200.0,T,214.5,M,14")] // cut off mid-field
    [InlineData("$IIMWV,083.0,R,13.8,N,A")] // cut off before the checksum
    [InlineData("$IIMWV")]
    public void A_partial_sentence_without_its_checksum_is_rejected(string line)
    {
        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(NmeaParseError.MissingChecksum, error);
    }

    [Theory]
    [InlineData(",R,13.8,N,A*0C")] // the tail of a cut-off sentence
    [InlineData("13.8,N,A")]
    [InlineData("garbage ~~~ 0x3F")]
    public void A_line_without_a_start_character_is_rejected(string line)
    {
        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(NmeaParseError.NoStartCharacter, error);
    }

    [Fact]
    public void A_cut_off_sentence_glued_to_a_whole_one_keeps_the_whole_one()
    {
        Assert.True(NmeaSentence.TryParse("$IIMWV,0$IIMTW,12.4,C*14", out var sentence, out _));
        Assert.Equal("MTW", sentence.Formatter);
        Assert.Equal("$IIMTW,12.4,C*14", sentence.Raw);
    }

    [Fact]
    public void A_whole_sentence_glued_to_a_cut_off_one_is_rejected()
    {
        // The last start character begins a sentence without its end.
        Assert.False(NmeaSentence.TryParse("$IIMTW,12.4,C*14$IIMWV,0", out _, out var error));
        Assert.Equal(NmeaParseError.MissingChecksum, error);
    }

    [Fact]
    public void A_missing_checksum_can_be_allowed()
    {
        var options = new NmeaParseOptions { RequireChecksum = false };

        Assert.True(NmeaSentence.TryParse("$IIMTW,12.4,C", out var sentence, out _, options));
        Assert.False(sentence.HasChecksum);
        Assert.Equal(["12.4", "C"], sentence.Fields);
        // A wrong checksum is still wrong.
        Assert.False(NmeaSentence.TryParse("$IIMTW,12.4,C*15", out _, out _, options));
    }

    [Fact]
    public void A_tag_block_is_skipped()
    {
        Assert.True(NmeaSentence.TryParse(@"\s:ownship,c:1793112601*41\$HEHDT,90.0,T*16", out var sentence, out _));
        Assert.Equal("HE", sentence.Talker);
        Assert.Equal("HDT", sentence.Formatter);
    }

    [Fact]
    public void Proprietary_and_encapsulated_sentences_parse()
    {
        var garmin = NmeaSentence.Parse("$PGRMZ,7,f,3*1C");
        Assert.True(garmin.IsProprietary);
        Assert.Equal("P", garmin.Talker);
        Assert.Equal("GRMZ", garmin.Formatter);

        var ais = NmeaSentence.Parse("!AIVDM,1,1,,A,15MgK45P3@G?fl0E`JbR0OwT0@MS,0*4E");
        Assert.Equal('!', ais.StartCharacter);
        Assert.Equal("AI", ais.Talker);
        Assert.Equal("VDM", ais.Formatter);
    }

    [Theory]
    [InlineData("$MWV,083.0,R,13.8,N,A")]
    [InlineData("$II-MWV,083.0,R,13.8,N,A")]
    [InlineData("$,083.0")]
    public void An_address_that_is_not_talker_and_formatter_is_rejected(string body)
    {
        // Give each a correct checksum so the address is what fails.
        string line = NmeaSentence.Format(body[1..]);

        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(NmeaParseError.InvalidAddress, error);
    }

    [Fact]
    public void Control_characters_are_rejected()
    {
        string line = NmeaSentence.Format("IIMTW,12.4\u0007,C");

        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(NmeaParseError.InvalidCharacter, error);
    }

    [Theory]
    [InlineData(null, NmeaParseError.Empty)]
    [InlineData("", NmeaParseError.Empty)]
    [InlineData("   ", NmeaParseError.Empty)]
    public void Empty_lines_are_rejected(string? line, NmeaParseError expected)
    {
        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void An_overlong_line_is_rejected()
    {
        string line = NmeaSentence.Format("IIXDR," + string.Join(',', Enumerable.Repeat("C,10.0,C,T", 60)));

        Assert.False(NmeaSentence.TryParse(line, out _, out var error));
        Assert.Equal(NmeaParseError.TooLong, error);
    }

    [Fact]
    public void Format_writes_the_checksum_parse_checks()
    {
        Assert.Equal("$IIMTW,12.4,C*14", NmeaSentence.Format("IIMTW,12.4,C"));
        Assert.Equal(0x14, NmeaSentence.ComputeChecksum("IIMTW,12.4,C"));
        Assert.Throws<FormatException>(() => NmeaSentence.Parse("$IIMTW,12.4,C*15"));
    }
}
