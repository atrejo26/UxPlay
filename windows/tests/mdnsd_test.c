/*
 * Tests for how UxPlay tells iPhones where to find this PC (lib/mdnsd), above all
 * the Mobile hotspot case: an iPhone on the PC's own hotspot must be given the
 * hotspot's address, and one on the main network the main address.
 *
 * Builds the real lib/mdnsd/mdnsd.c in, to reach its internal helpers:
 *   gcc -std=gnu11 -Wall -Wextra -Werror -I lib windows/tests/mdnsd_test.c -o mdnsd_test [-lws2_32 -liphlpapi] -lpthread
 * Runs on Windows (CI) and Linux; the adapter checks are Windows-only.
 */

#include "../../lib/mdnsd/mdnsd.c"

static int failures = 0;
static int checks = 0;

#define CHECK(cond) do { \
        checks++; \
        if (!(cond)) { \
            failures++; \
            fprintf(stderr, "FAILED %s:%d: %s\n", __FILE__, __LINE__, #cond); \
        } \
    } while (0)

static uint32_t ip(const char *text)
{
    struct in_addr a;
    inet_pton(AF_INET, text, &a);
    return a.s_addr;
}

/* Does the packet hold an A record with this address (4 bytes after type/class/ttl/rdlength 4)? */
static int has_a_record(const unsigned char *bytes, size_t length, uint32_t addr)
{
    const unsigned char *a = (const unsigned char *) &addr;
    for (size_t i = 0; i + 14 <= length; i++) {
        if (bytes[i] == 0 && bytes[i + 1] == DNS_TYPE_A &&
            bytes[i + 8] == 0 && bytes[i + 9] == 4 &&
            memcmp(bytes + i + 10, a, 4) == 0) {
            return 1;
        }
    }
    return 0;
}

static mdnsd_t *test_daemon(void)
{
    mdnsd_t *mdnsd = mdnsd_init("UxPlay-Test.local");
    mdnsd_service_t airplay;
    mdnsd_service_t raop;

    memset(&airplay, 0, sizeof(airplay));
    memset(&raop, 0, sizeof(raop));
    snprintf(airplay.type, sizeof(airplay.type), "_airplay._tcp.local");
    snprintf(airplay.name, sizeof(airplay.name), "UxPlay-Test._airplay._tcp.local");
    airplay.port = 7000;
    airplay.registered = 1;
    mdnsd_txt_add(&airplay.txt, "model", "AppleTV3,2");
    snprintf(raop.type, sizeof(raop.type), "_raop._tcp.local");
    snprintf(raop.name, sizeof(raop.name), "AABBCCDDEEFF@UxPlay-Test._raop._tcp.local");
    raop.port = 7000;
    raop.registered = 1;
    mdnsd_txt_add(&raop.txt, "am", "AppleTV3,2");
    mdnsd_set_services(mdnsd, &airplay, &raop);

    /* the main network (campus Ethernet) and the PC's Mobile hotspot */
    mdnsd->links4[0].addr = ip("172.24.128.27");
    mdnsd->links4[0].mask = ip("255.255.128.0");
    mdnsd->links4[1].addr = ip("192.168.137.1");
    mdnsd->links4[1].mask = ip("255.255.255.0");
    mdnsd->nlinks4 = 2;
    return mdnsd;
}

static void test_prefix_mask(void)
{
    CHECK(mdns_prefix_mask(0) == 0);
    CHECK(mdns_prefix_mask(24) == ip("255.255.255.0"));
    CHECK(mdns_prefix_mask(17) == ip("255.255.128.0"));
    CHECK(mdns_prefix_mask(1) == ip("128.0.0.0"));
    CHECK(mdns_prefix_mask(32) == ip("255.255.255.255"));
    CHECK(mdns_prefix_mask(255) == ip("255.255.255.255"));
}

static void test_link_for_sender(void)
{
    mdnsd_t *mdnsd = test_daemon();
    const mdns_link4_t *l = mdnsd->links4;

    CHECK(mdns_link4_for(l, 2, ip("192.168.137.45")) == 1);   /* iPhone on the hotspot */
    CHECK(mdns_link4_for(l, 2, ip("192.168.137.254")) == 1);
    CHECK(mdns_link4_for(l, 2, ip("172.24.200.9")) == 0);     /* on the main network */
    CHECK(mdns_link4_for(l, 2, ip("172.24.34.100")) == 0);    /* elsewhere: the main one */
    CHECK(mdns_link4_for(l, 2, ip("192.168.138.5")) == 0);    /* next subnet up */
    CHECK(mdns_link4_for(l, 1, ip("192.168.137.45")) == 0);   /* no hotspot running */
    CHECK(mdns_link4_for(l, 0, ip("192.168.137.45")) == 0);

    mdnsd->links4[1].mask = 0;                                 /* unknown subnet: never matches */
    CHECK(mdns_link4_for(l, 2, ip("192.168.137.45")) == 0);
    mdnsd_destroy(mdnsd);
}

static void test_answer_holds_the_address_on_that_network(void)
{
    mdnsd_t *mdnsd = test_daemon();
    mdns_packet_t packet;

    mdns_use_link4_locked(mdnsd, 1);
    CHECK(mdns_current_ipv4(mdnsd) == ip("192.168.137.1"));
    CHECK(mdns_build_combined_response(mdnsd, &packet, 1, 1, 1, MDNSD_TTL_SERVICE, MDNS_FAMILY_IPV4) == 0);
    CHECK(has_a_record(packet.bytes, packet.length, ip("192.168.137.1")));
    CHECK(!has_a_record(packet.bytes, packet.length, ip("172.24.128.27")));

    mdns_use_link4_locked(mdnsd, 0);
    CHECK(mdns_build_combined_response(mdnsd, &packet, 1, 1, 1, MDNSD_TTL_SERVICE, MDNS_FAMILY_IPV4) == 0);
    CHECK(has_a_record(packet.bytes, packet.length, ip("172.24.128.27")));
    CHECK(!has_a_record(packet.bytes, packet.length, ip("192.168.137.1")));

    /* out of range falls back to the main network */
    mdns_use_link4_locked(mdnsd, 7);
    CHECK(mdnsd->link4 == 0);
    mdns_use_link4_locked(mdnsd, -1);
    CHECK(mdnsd->link4 == 0);

    /* nothing found at all: no A record, as before */
    mdnsd->nlinks4 = 0;
    CHECK(mdns_current_ipv4(mdnsd) == 0);
    mdnsd_destroy(mdnsd);
}

static void test_update_links(void)
{
    mdnsd_t *mdnsd = test_daemon();
    mdns_link4_t links[MDNS_MAX_LINKS4];

    memcpy(links, mdnsd->links4, sizeof(links));
    CHECK(mdns_update_links4_locked(mdnsd, links, 2) == 0);   /* nothing new */

    links[1].addr = 0;
    CHECK(mdns_update_links4_locked(mdnsd, links, 1) == 1);   /* hotspot turned off */
    CHECK(mdnsd->nlinks4 == 1);
    CHECK(mdnsd->links4[0].addr == ip("172.24.128.27"));

    links[1].addr = ip("192.168.137.1");
    links[1].mask = ip("255.255.255.0");
    CHECK(mdns_update_links4_locked(mdnsd, links, 2) == 1);   /* and on again */
    CHECK(mdnsd->nlinks4 == 2);
    CHECK(mdnsd->links4[1].addr == ip("192.168.137.1"));

    links[0].addr = ip("172.24.130.4");                        /* main address renewed */
    CHECK(mdns_update_links4_locked(mdnsd, links, 2) == 1);
    CHECK(mdnsd->links4[0].addr == ip("172.24.130.4"));
    CHECK(mdnsd->link4 == 0);

    CHECK(mdns_update_links4_locked(mdnsd, links, 0) == 0);   /* nonsense is ignored */
    CHECK(mdns_update_links4_locked(mdnsd, links, MDNS_MAX_LINKS4 + 1) == 0);
    CHECK(mdnsd->nlinks4 == 2);
    mdnsd_destroy(mdnsd);
}

/* A real query over the loopback network, as an iPhone sends it: the reply that comes
 * back must name the address on the sender's network. */
static void test_query_round_trip(void)
{
    mdnsd_t *mdnsd = test_daemon();
    struct sockaddr_in phone_addr;
    struct sockaddr_in pc_addr;
    socklen_t len = sizeof(phone_addr);
    unsigned char query[64];
    size_t q = 0;
    unsigned char reply[MDNS_MAX_PACKET];
    int got;
    int phone = (int) socket(AF_INET, SOCK_DGRAM, 0);
    int pc = (int) socket(AF_INET, SOCK_DGRAM, 0);
    CHECK(phone != -1 && pc != -1);

    memset(&phone_addr, 0, sizeof(phone_addr));
    phone_addr.sin_family = AF_INET;
    phone_addr.sin_addr.s_addr = ip("127.0.0.1");
    CHECK(bind(phone, (struct sockaddr *) &phone_addr, sizeof(phone_addr)) == 0);
    CHECK(getsockname(phone, (struct sockaddr *) &phone_addr, &len) == 0);
    pc_addr = phone_addr;
    pc_addr.sin_port = 0;
    CHECK(bind(pc, (struct sockaddr *) &pc_addr, sizeof(pc_addr)) == 0);

    /* pretend 127.0.0.0/8 is the hotspot this PC runs, at 127.0.0.2 */
    mdnsd->links4[1].addr = ip("127.0.0.2");
    mdnsd->links4[1].mask = ip("255.0.0.0");
    mdnsd->sock_fd4 = pc;

    /* "where is UxPlay-Test.local?" (A, asking for a direct reply) */
    memset(query, 0, sizeof(query));
    query[5] = 1;                       /* one question */
    q = 12;
    query[q++] = 11; memcpy(query + q, "UxPlay-Test", 11); q += 11;
    query[q++] = 5;  memcpy(query + q, "local", 5); q += 5;
    query[q++] = 0;
    query[q++] = 0; query[q++] = DNS_TYPE_A;
    query[q++] = 0x80; query[q++] = DNS_CLASS_IN;

    mdns_handle_query(mdnsd, query, q, MDNS_FAMILY_IPV4, &phone_addr, phone_addr.sin_port);

    {
        fd_set rfds;
        struct timeval tv = { 2, 0 };
        FD_ZERO(&rfds);
        FD_SET(phone, &rfds);
        CHECK(select(phone + 1, &rfds, NULL, NULL, &tv) == 1);
    }
    got = (int) recv(phone, (char *) reply, sizeof(reply), 0);
    CHECK(got > 12);
    if (got > 12) {
        CHECK(has_a_record(reply, (size_t) got, ip("127.0.0.2")));
        CHECK(!has_a_record(reply, (size_t) got, ip("172.24.128.27")));
    }
    CHECK(mdnsd->link4 == 0);           /* back on the main network afterwards */

    /* the same query from a sender on no hotspot gets the main address */
    mdnsd->links4[1].addr = ip("192.168.137.1");
    mdnsd->links4[1].mask = ip("255.255.255.0");
    mdns_handle_query(mdnsd, query, q, MDNS_FAMILY_IPV4, &phone_addr, phone_addr.sin_port);
    {
        fd_set rfds;
        struct timeval tv = { 2, 0 };
        FD_ZERO(&rfds);
        FD_SET(phone, &rfds);
        CHECK(select(phone + 1, &rfds, NULL, NULL, &tv) == 1);
    }
    got = (int) recv(phone, (char *) reply, sizeof(reply), 0);
    CHECK(got > 12 && has_a_record(reply, (size_t) got, ip("172.24.128.27")));

    mdnsd->sock_fd4 = -1;
    CLOSESOCKET(pc);
    CLOSESOCKET(phone);
    mdnsd_destroy(mdnsd);
}

#ifdef _WIN32
static IP_ADAPTER_ADDRESSES adapter(const wchar_t *description, const wchar_t *name, int up,
                                    IP_ADAPTER_UNICAST_ADDRESS *unicast)
{
    IP_ADAPTER_ADDRESSES a;
    memset(&a, 0, sizeof(a));
    a.Description = (PWCHAR) description;
    a.FriendlyName = (PWCHAR) name;
    a.OperStatus = up ? IfOperStatusUp : IfOperStatusDown;
    a.IfType = IF_TYPE_IEEE80211;
    a.FirstUnicastAddress = unicast;
    return a;
}

static void test_windows_adapters(void)
{
    struct sockaddr_in self_assigned, hotspot_ip, lan_ip;
    IP_ADAPTER_UNICAST_ADDRESS u_hot, u_self, u_lan;
    uint32_t mask;

    memset(&self_assigned, 0, sizeof(self_assigned));
    self_assigned.sin_family = AF_INET;
    self_assigned.sin_addr.s_addr = ip("169.254.10.20");
    hotspot_ip = self_assigned;
    hotspot_ip.sin_addr.s_addr = ip("192.168.137.1");
    lan_ip = self_assigned;
    lan_ip.sin_addr.s_addr = ip("192.168.1.20");

    memset(&u_hot, 0, sizeof(u_hot));
    memset(&u_self, 0, sizeof(u_self));
    memset(&u_lan, 0, sizeof(u_lan));
    u_self.Address.lpSockaddr = (LPSOCKADDR) &self_assigned;
    u_self.OnLinkPrefixLength = 16;
    u_self.Next = &u_hot;               /* a self-assigned address first, then the real one */
    u_hot.Address.lpSockaddr = (LPSOCKADDR) &hotspot_ip;
    u_hot.OnLinkPrefixLength = 24;
    u_lan.Address.lpSockaddr = (LPSOCKADDR) &lan_ip;
    u_lan.OnLinkPrefixLength = 24;

    IP_ADAPTER_ADDRESSES hot = adapter(L"Microsoft Wi-Fi Direct Virtual Adapter #2",
                                       L"Local Area Connection* 10", 1, &u_self);
    IP_ADAPTER_ADDRESSES hosted = adapter(L"Microsoft Hosted Network Virtual Adapter",
                                          L"Local Area Connection* 2", 1, &u_lan);
    IP_ADAPTER_ADDRESSES off = adapter(L"Microsoft Wi-Fi Direct Virtual Adapter",
                                       L"Local Area Connection* 1", 0, &u_hot);
    IP_ADAPTER_ADDRESSES lan = adapter(L"Intel(R) Ethernet Connection I219-V", L"Ethernet", 1, &u_lan);
    IP_ADAPTER_ADDRESSES ics_lan = adapter(L"Realtek USB GbE Family Controller", L"Ethernet 2", 1, &u_hot);

    /* the address skips 169.254.x.x and brings its subnet */
    CHECK(mdns_adapter_ipv4(&hot, &mask) == ip("192.168.137.1"));
    CHECK(mask == ip("255.255.255.0"));
    u_self.Next = NULL;
    CHECK(mdns_adapter_ipv4(&hot, &mask) == 0);
    CHECK(mask == 0);
    u_self.Next = &u_hot;

    CHECK(mdns_is_hotspot_adapter(&hot, ip("192.168.137.1")));
    CHECK(mdns_is_hotspot_adapter(&hosted, ip("192.168.1.20")));       /* older Windows 10 */
    CHECK(mdns_is_hotspot_adapter(&ics_lan, ip("192.168.137.1")));     /* sharing over a cable */
    CHECK(!mdns_is_hotspot_adapter(&off, ip("192.168.137.1")));        /* hotspot turned off */
    CHECK(!mdns_is_hotspot_adapter(&hot, 0));                          /* no address yet */
    CHECK(!mdns_is_hotspot_adapter(&lan, ip("192.168.1.20")));         /* an ordinary network */

    /* the hotspot is never taken for the main network */
    CHECK(mdns_is_virtual_adapter(&hot));
    CHECK(!mdns_is_virtual_adapter(&lan));
}

static void test_windows_real_adapters(void)
{
    mdns_link4_t links[MDNS_MAX_LINKS4];
    int count = mdns_get_links4(links, MDNS_MAX_LINKS4, 0);
    CHECK(count >= 1 && count <= MDNS_MAX_LINKS4);
    for (int i = 1; i < count; i++) {
        CHECK(links[i].addr != 0 && links[i].mask != 0);
    }
}
#endif

int main(void)
{
#ifdef _WIN32
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        fprintf(stderr, "WSAStartup failed\n");
        return 2;
    }
#endif
    test_prefix_mask();
    test_link_for_sender();
    test_answer_holds_the_address_on_that_network();
    test_update_links();
    test_query_round_trip();
#ifdef _WIN32
    test_windows_adapters();
    test_windows_real_adapters();
    WSACleanup();
#endif
    printf("mdnsd tests: %d checks, %d failed\n", checks, failures);
    return failures ? 1 : 0;
}
