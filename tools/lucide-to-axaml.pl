# SPDX-License-Identifier: GPL-3.0-or-later
# Gör om Lucide-ikoner (SVG, ISC-licens, https://lucide.dev) till Avalonia-geometrier i Icons.axaml.
# Ikonerna är linjeikoner (24x24, linjebredd 2); de ritas med Path + Stroke i programmet (se Classes="ico").
# Användning: perl tools/lucide-to-axaml.pl <mapp med .svg> > src/Cyberdrome.App/Assets/Icons.axaml
use strict; use warnings;
my $dir = shift or die "mapp?";
opendir(my $dh, $dir) or die $!;
my @files = sort grep { /\.svg$/ } readdir $dh;
sub attr { my ($tag, $n) = @_; return $tag =~ /\b$n="([^"]*)"/ ? $1 : undef; }
print qq{<!-- Genererad av tools/lucide-to-axaml.pl från Lucide (ISC-licens, se legal/THIRD-PARTY-NOTICES.txt). Ändra inte för hand. -->\n};
print qq{<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n};
for my $f (@files) {
    open(my $fh, '<', "$dir/$f") or die $!; local $/; my $svg = <$fh>; close $fh;
    (my $name = $f) =~ s/\.svg$//;
    my @parts;
    while ($svg =~ /<(path|circle|rect|line|polyline|polygon|ellipse)\b([^>]*)\/?>/g) {
        my ($t, $a) = ($1, $2);
        if ($t eq q(path)) { my $d = attr($a, q(d)); my $n = qr/-?(?:\d+\.?\d*|\.\d+)/; $d =~ s/^\s*m\s*($n)[\s,]*($n)\s*(?=[-.\d])/M$1 $2 l/ or $d =~ s/^\s*m/M/; push @parts, $d; } # första m i en path är absolut i SVG
        elsif ($t eq 'circle' || $t eq 'ellipse') {
            my ($cx, $cy) = (attr($a, 'cx'), attr($a, 'cy'));
            my $rx = attr($a, 'r') // attr($a, 'rx'); my $ry = attr($a, 'r') // attr($a, 'ry');
            push @parts, sprintf("M%s,%s a%s %s 0 1 0 %s 0 a%s %s 0 1 0 %s 0 Z", $cx - $rx, $cy, $rx, $ry, 2 * $rx, $rx, $ry, -2 * $rx);
        }
        elsif ($t eq 'rect') {
            my ($x, $y, $w, $h) = map { attr($a, $_) // 0 } qw(x y width height);
            my $r = attr($a, 'rx') // attr($a, 'ry') // 0;
            if ($r > 0) {
                push @parts, sprintf("M%s,%s h%s a%s,%s 0 0 1 %s,%s v%s a%s,%s 0 0 1 %s,%s h%s a%s,%s 0 0 1 %s,%s v%s a%s,%s 0 0 1 %s,%s Z",
                    $x + $r, $y, $w - 2 * $r, $r, $r, $r, $r, $h - 2 * $r, $r, $r, -$r, $r, -($w - 2 * $r), $r, $r, -$r, -$r, -($h - 2 * $r), $r, $r, $r, -$r);
            } else { push @parts, "M$x,$y h$w v$h h-$w Z"; }
        }
        elsif ($t eq 'line') { push @parts, sprintf("M%s,%s L%s,%s", map { attr($a, $_) } qw(x1 y1 x2 y2)); }
        elsif ($t eq 'polyline' || $t eq 'polygon') {
            my @p = split /[\s,]+/, attr($a, 'points'); my $d = "M$p[0],$p[1]";
            for (my $i = 2; $i < @p; $i += 2) { $d .= " L$p[$i],$p[$i+1]"; }
            $d .= " Z" if $t eq 'polygon'; push @parts, $d;
        }
    }
    my $d = join(' ', @parts); $d =~ s/\s+/ /g;
    print qq{  <StreamGeometry x:Key="Icon.$name">F0 $d</StreamGeometry>\n};
}
print "</ResourceDictionary>\n";
