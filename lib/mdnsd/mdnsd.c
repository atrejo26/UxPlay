/**
 *  Copyright (C) 2026  kgbook
 *
 *  This library is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 2.1 of the License, or (at your option) any later version.
 *
 *  This library is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 *  Lesser General Public License for more details.
 */

#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifndef WIN32
#include <ifaddrs.h>
#include <net/if.h>
#endif

#include "../compat.h"
#include "mdnsd.h"

#define MDNS_ADDR4 "224.0.0.251"
#define MDNS_ADDR6 "ff02::fb"
#define MDNS_PORT 5353
#define MDNS_MAX_PACKET 1500
#define MDNS_COMBINED_PACKET_MAX 1200
#define MDNS_TTL_HOST 120
#define MDNS_MAX_LINKS4 4            /* the main network plus a few hotspots */
#define MDNS_REFRESH_SECONDS 5       /* how often to look for a hotspot turned on later */
#define MDNS_HOTSPOT_ADDR 0xC0A88901 /* 192.168.137.1: Windows Mobile hotspot (Internet Connection Sharing) */

#define DNS_TYPE_A 1
#define DNS_TYPE_AAAA 28
#define DNS_TYPE_PTR 12
#define DNS_TYPE_TXT 16
#define DNS_TYPE_SRV 33
#define DNS_TYPE_ANY 255
#define DNS_CLASS_IN 1
#define DNS_FLAG_RESPONSE 0x8000
#define DNS_FLAG_AUTHORITATIVE 0x0400
#define DNS_CACHE_FLUSH 0x8000
#define DNS_UNICAST_RESPONSE 0x8000

typedef struct {
    unsigned char bytes[MDNS_MAX_PACKET];
    size_t length;
} mdns_packet_t;

/* An IPv4 network this PC is advertised on: its own address there and the subnet
 * mask (both in network byte order; a mask of 0 means unknown). */
typedef struct {
    uint32_t addr;
    uint32_t mask;
} mdns_link4_t;

typedef enum {
    MDNS_FAMILY_IPV4,
    MDNS_FAMILY_IPV6
} mdns_family_t;

struct mdnsd_s {
    char host_name[MDNSD_MAX_NAME];
    mdnsd_service_t airplay;
    mdnsd_service_t raop;

    /* links4[0] is the main network (on Windows, the one Windows prefers). The others
     * are Mobile hotspots this PC runs: a phone joined to one can't reach the main
     * address, so it is told the hotspot's own address. link4 is the one being sent on. */
    mdns_link4_t links4[MDNS_MAX_LINKS4];
    int nlinks4;
    int link4;
    time_t links4_checked;
    unsigned char ipv6_addr[16];
    unsigned int ipv6_scope_id;
    int sock_fd4;
    int sock_fd6;
    int running;
    thread_handle_t thread;
    mutex_handle_t mutex;
};

static int mdns_put_u8(mdns_packet_t *packet, unsigned int value)
{
    if (packet->length + 1 > sizeof(packet->bytes)) {
        return -1;
    }
    packet->bytes[packet->length++] = (unsigned char) value;
    return 0;
}

static int mdns_put_u16(mdns_packet_t *packet, unsigned int value)
{
    if (packet->length + 2 > sizeof(packet->bytes)) {
        return -1;
    }
    packet->bytes[packet->length++] = (unsigned char) ((value >> 8) & 0xff);
    packet->bytes[packet->length++] = (unsigned char) (value & 0xff);
    return 0;
}

static int mdns_put_u32(mdns_packet_t *packet, uint32_t value)
{
    if (packet->length + 4 > sizeof(packet->bytes)) {
        return -1;
    }
    packet->bytes[packet->length++] = (unsigned char) ((value >> 24) & 0xff);
    packet->bytes[packet->length++] = (unsigned char) ((value >> 16) & 0xff);
    packet->bytes[packet->length++] = (unsigned char) ((value >> 8) & 0xff);
    packet->bytes[packet->length++] = (unsigned char) (value & 0xff);
    return 0;
}

static int mdns_put_bytes(mdns_packet_t *packet, const void *bytes, size_t length)
{
    if (packet->length + length > sizeof(packet->bytes)) {
        return -1;
    }
    memcpy(packet->bytes + packet->length, bytes, length);
    packet->length += length;
    return 0;
}

static int mdns_put_name(mdns_packet_t *packet, const char *name)
{
    const char *label = name;

    while (label && *label) {
        const char *dot = strchr(label, '.');
        size_t length = dot ? (size_t) (dot - label) : strlen(label);

        if (length > 63 || mdns_put_u8(packet, (unsigned int) length)) {
            return -1;
        }
        if (mdns_put_bytes(packet, label, length)) {
            return -1;
        }
        if (!dot) {
            break;
        }
        label = dot + 1;
    }

    return mdns_put_u8(packet, 0);
}

static int mdns_begin_rr(mdns_packet_t *packet, const char *name, unsigned int type,
                         unsigned int cls, uint32_t ttl, size_t *rdlength_pos)
{
    if (mdns_put_name(packet, name) ||
        mdns_put_u16(packet, type) ||
        mdns_put_u16(packet, cls) ||
        mdns_put_u32(packet, ttl)) {
        return -1;
    }
    *rdlength_pos = packet->length;
    return mdns_put_u16(packet, 0);
}

static void mdns_end_rr(mdns_packet_t *packet, size_t rdlength_pos)
{
    size_t rdlength = packet->length - rdlength_pos - 2;
    packet->bytes[rdlength_pos] = (unsigned char) ((rdlength >> 8) & 0xff);
    packet->bytes[rdlength_pos + 1] = (unsigned char) (rdlength & 0xff);
}

static int mdns_add_ptr(mdns_packet_t *packet, const char *name, const char *ptr,
                        uint32_t ttl)
{
    size_t rdlength_pos;
    if (mdns_begin_rr(packet, name, DNS_TYPE_PTR, DNS_CLASS_IN, ttl, &rdlength_pos) ||
        mdns_put_name(packet, ptr)) {
        return -1;
    }
    mdns_end_rr(packet, rdlength_pos);
    return 0;
}

static int mdns_add_srv(mdns_packet_t *packet, const char *name, unsigned short port,
                        const char *target, uint32_t ttl)
{
    size_t rdlength_pos;
    if (mdns_begin_rr(packet, name, DNS_TYPE_SRV, DNS_CLASS_IN | DNS_CACHE_FLUSH,
                      ttl, &rdlength_pos) ||
        mdns_put_u16(packet, 0) ||
        mdns_put_u16(packet, 0) ||
        mdns_put_u16(packet, port) ||
        mdns_put_name(packet, target)) {
        return -1;
    }
    mdns_end_rr(packet, rdlength_pos);
    return 0;
}

static int mdns_add_txt(mdns_packet_t *packet, const char *name, const mdnsd_txt_t *txt,
                        uint32_t ttl)
{
    size_t rdlength_pos;
    if (mdns_begin_rr(packet, name, DNS_TYPE_TXT, DNS_CLASS_IN | DNS_CACHE_FLUSH,
                      ttl, &rdlength_pos) ||
        mdns_put_bytes(packet, txt->bytes, (size_t) txt->length)) {
        return -1;
    }
    mdns_end_rr(packet, rdlength_pos);
    return 0;
}

static int mdns_add_a(mdns_packet_t *packet, const char *name, uint32_t addr,
                      uint32_t ttl)
{
    size_t rdlength_pos;
    if (!addr) {
        return 0;
    }
    if (mdns_begin_rr(packet, name, DNS_TYPE_A, DNS_CLASS_IN | DNS_CACHE_FLUSH,
                      ttl, &rdlength_pos) ||
        mdns_put_bytes(packet, &addr, sizeof(addr))) {
        return -1;
    }
    mdns_end_rr(packet, rdlength_pos);
    return 0;
}

static int mdns_has_ipv6_addr(const unsigned char addr[16])
{
    static const unsigned char zero[16] = {0};
    return memcmp(addr, zero, sizeof(zero)) != 0;
}

static int mdns_add_aaaa(mdns_packet_t *packet, const char *name,
                         const unsigned char addr[16], uint32_t ttl)
{
    size_t rdlength_pos;

    if (!mdns_has_ipv6_addr(addr)) {
        return 0;
    }
    if (mdns_begin_rr(packet, name, DNS_TYPE_AAAA,
                      DNS_CLASS_IN | DNS_CACHE_FLUSH, ttl, &rdlength_pos) ||
        mdns_put_bytes(packet, addr, 16)) {
        return -1;
    }
    mdns_end_rr(packet, rdlength_pos);
    return 0;
}

/* The address to put in A records: the one on the network being sent on */
static uint32_t mdns_current_ipv4(const mdnsd_t *mdnsd)
{
    if (mdnsd->link4 < 0 || mdnsd->link4 >= mdnsd->nlinks4) {
        return 0;
    }
    return mdnsd->links4[mdnsd->link4].addr;
}

/* A subnet mask, in network byte order, from a prefix length such as 24 */
static inline uint32_t mdns_prefix_mask(unsigned int prefix_length)
{
    if (prefix_length == 0) {
        return 0;
    }
    if (prefix_length >= 32) {
        return 0xFFFFFFFFu;
    }
    return htonl(0xFFFFFFFFu << (32 - prefix_length));
}

/* Which link a query came in on, from the sender's address (network byte order):
 * a hotspot whose subnet holds it, otherwise the main network. */
static int mdns_link4_for(const mdns_link4_t *links, int count, uint32_t from)
{
    for (int i = 1; i < count; i++) {
        if (links[i].mask && (from & links[i].mask) == (links[i].addr & links[i].mask)) {
            return i;
        }
    }
    return 0;
}

static int mdns_add_host_records(mdnsd_t *mdnsd, mdns_packet_t *packet,
                                 uint32_t ttl, unsigned int *answers,
                                 mdns_family_t family)
{
    /*
     * Treat the IPv4 and IPv6 sockets as logical mDNS interfaces. This keeps
     * AirPlay clients from seeing cross-family host records in one response
     * and opening two TCP connections for the same service.
     */
    if (family == MDNS_FAMILY_IPV4) {
        uint32_t ipv4_addr = mdns_current_ipv4(mdnsd);
        if (mdns_add_a(packet, mdnsd->host_name, ipv4_addr, ttl)) {
            return -1;
        }
        if (ipv4_addr) {
            (*answers)++;
        }
    } else {
        if (mdns_add_aaaa(packet, mdnsd->host_name, mdnsd->ipv6_addr, ttl)) {
            return -1;
        }
        if (mdns_has_ipv6_addr(mdnsd->ipv6_addr)) {
            (*answers)++;
        }
    }

    return 0;
}

static uint16_t mdns_read_u16(const unsigned char *bytes)
{
    return (uint16_t) (((uint16_t) bytes[0] << 8) | bytes[1]);
}

static int mdns_read_name(const unsigned char *packet, size_t packet_len,
                          size_t *offset, char *name, size_t name_len)
{
    size_t pos = *offset;
    size_t out = 0;
    int jumped = 0;
    int jumps = 0;

    while (pos < packet_len) {
        unsigned int length = packet[pos++];

        if (length == 0) {
            if (!jumped) {
                *offset = pos;
            }
            if (out == 0) {
                if (name_len < 2) {
                    return -1;
                }
                name[out++] = '.';
            }
            name[out] = '\0';
            return 0;
        }

        if ((length & 0xc0) == 0xc0) {
            unsigned int ptr;
            if (pos >= packet_len || ++jumps > 16) {
                return -1;
            }
            ptr = ((length & 0x3f) << 8) | packet[pos++];
            if (ptr >= packet_len) {
                return -1;
            }
            if (!jumped) {
                *offset = pos;
            }
            pos = ptr;
            jumped = 1;
            continue;
        }

        if ((length & 0xc0) || pos + length > packet_len) {
            return -1;
        }
        if (out) {
            if (out + 1 >= name_len) {
                return -1;
            }
            name[out++] = '.';
        }
        if (out + length >= name_len) {
            return -1;
        }
        memcpy(name + out, packet + pos, length);
        out += length;
        pos += length;
    }

    return -1;
}

static int mdns_name_equals(const char *left, const char *right)
{
    while (*left && *right) {
        if (tolower((unsigned char) *left) != tolower((unsigned char) *right)) {
            return 0;
        }
        left++;
        right++;
    }
    return *left == '\0' && *right == '\0';
}

static int mdns_query_type_matches(uint16_t query_type, uint16_t record_type)
{
    return query_type == record_type || query_type == DNS_TYPE_ANY;
}

#ifdef _WIN32
/* Adapters that are not the PC's own Wi-Fi or Ethernet: Hyper-V/WSL switches,
 * virtual machines and VPN tunnels. An iPhone on the local network can't reach
 * an address on one of these, so it must never be the one advertised. */
static int mdns_is_virtual_adapter(const IP_ADAPTER_ADDRESSES *a)
{
    static const wchar_t *const names[] = {
        L"Hyper-V", L"Virtual", L"WSL", L"vEthernet", L"VPN", L"TAP-", L"Wintun",
        L"WireGuard", L"Tailscale", L"ZeroTier", L"Loopback", NULL
    };
    if (a->IfType == IF_TYPE_SOFTWARE_LOOPBACK || a->IfType == IF_TYPE_TUNNEL ||
        a->IfType == IF_TYPE_PPP) {
        return 1;
    }
    for (int i = 0; names[i]; i++) {
        if ((a->Description && wcsstr(a->Description, names[i])) ||
            (a->FriendlyName && wcsstr(a->FriendlyName, names[i]))) {
            return 1;
        }
    }
    return 0;
}

/* The adapter's first usable IPv4 address (not a self-assigned 169.254.x.x one), or 0.
 * Its subnet mask goes in *mask. */
static uint32_t mdns_adapter_ipv4(const IP_ADAPTER_ADDRESSES *a, uint32_t *mask)
{
    for (const IP_ADAPTER_UNICAST_ADDRESS *u = a->FirstUnicastAddress; u; u = u->Next) {
        if (u->Address.lpSockaddr && u->Address.lpSockaddr->sa_family == AF_INET) {
            uint32_t addr = ((const struct sockaddr_in *) u->Address.lpSockaddr)->sin_addr.s_addr;
            uint32_t host = ntohl(addr);
            if (host != 0 && (host >> 16) != 0xA9FE) {   /* 169.254.0.0/16 */
                *mask = mdns_prefix_mask(u->OnLinkPrefixLength);
                return addr;
            }
        }
    }
    *mask = 0;
    return 0;
}

/* The adapter Windows' Mobile hotspot shares a connection through: it has the
 * hotspot's own address, 192.168.137.1. The iPhones joined to the hotspot are on
 * this adapter, and nothing else on the PC can reach them. */
static int mdns_is_hotspot_adapter(const IP_ADAPTER_ADDRESSES *a, uint32_t addr)
{
    static const wchar_t *const names[] = {
        L"Wi-Fi Direct Virtual Adapter", L"Hosted Network Virtual Adapter", NULL
    };
    if (a->OperStatus != IfOperStatusUp || !addr) {
        return 0;
    }
    if (ntohl(addr) == MDNS_HOTSPOT_ADDR) {
        return 1;
    }
    for (int i = 0; names[i]; i++) {
        if (a->Description && wcsstr(a->Description, names[i])) {
            return 1;
        }
    }
    return 0;
}

/* Fills links with the networks to advertise on: first the main one, then any Mobile
 * hotspot this PC runs. Returns how many, or -1 if Windows could not list its adapters. */
static int mdns_get_links4(mdns_link4_t *links, int max, int quiet)
{
    ULONG buflen = 15000;
    PIP_ADAPTER_ADDRESSES pAddresses = NULL;
    DWORD dwRetVal = ERROR_BUFFER_OVERFLOW;
    int count = 1;

    memset(links, 0, sizeof(*links) * (size_t) max);

    /* the adapter list can grow between calls, so retry a few times */
    for (int tries = 0; tries < 3 && dwRetVal == ERROR_BUFFER_OVERFLOW; tries++) {
        free(pAddresses);
        pAddresses = (IP_ADAPTER_ADDRESSES *) malloc(buflen);
        if (!pAddresses) {
            return -1;
        }
        dwRetVal = GetAdaptersAddresses(AF_INET, GAA_FLAG_INCLUDE_GATEWAYS | GAA_FLAG_SKIP_ANYCAST |
                                        GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER,
                                        NULL, pAddresses, &buflen);
    }
    if (dwRetVal != NO_ERROR) {
        free(pAddresses);
        return -1;
    }

    /* Of the connected Wi-Fi/Ethernet adapters with a default gateway, take the
     * one Windows itself prefers: the lowest route metric. Only if there is none,
     * fall back to any other connected adapter (e.g. a PC whose only network
     * is a tunnel), rather than advertising no address at all. */
    const IP_ADAPTER_ADDRESSES *best = NULL;
    for (int strict = 1; strict >= 0 && !best; strict--) {
        for (const IP_ADAPTER_ADDRESSES *pCurr = pAddresses; pCurr; pCurr = pCurr->Next) {
            uint32_t mask;
            if (pCurr->OperStatus != IfOperStatusUp || pCurr->FirstGatewayAddress == NULL ||
                pCurr->IfType == IF_TYPE_SOFTWARE_LOOPBACK ||
                (strict && mdns_is_virtual_adapter(pCurr))) {
                continue;
            }
            uint32_t candidate = mdns_adapter_ipv4(pCurr, &mask);
            if (candidate && (!best || pCurr->Ipv4Metric < best->Ipv4Metric)) {
                best = pCurr;
                links[0].addr = candidate;
                links[0].mask = mask;
            }
        }
    }
    if (best && !quiet) {
        unsigned char *b = (unsigned char *) &links[0].addr;
        fprintf(stderr, "mDNS: advertising on %u.%u.%u.%u (%ls)\n", b[0], b[1], b[2], b[3],
                best->FriendlyName ? best->FriendlyName : L"network adapter");
    }

    for (const IP_ADAPTER_ADDRESSES *pCurr = pAddresses; pCurr && count < max; pCurr = pCurr->Next) {
        uint32_t mask;
        uint32_t addr;
        int seen = 0;
        if (pCurr == best) {
            continue;
        }
        addr = mdns_adapter_ipv4(pCurr, &mask);
        if (!mdns_is_hotspot_adapter(pCurr, addr)) {
            continue;
        }
        for (int i = 0; i < count; i++) {
            seen |= (links[i].addr == addr);
        }
        if (seen) {
            continue;
        }
        links[count].addr = addr;
        links[count].mask = mask ? mask : mdns_prefix_mask(24);
        if (!quiet) {
            unsigned char *b = (unsigned char *) &addr;
            fprintf(stderr, "mDNS: also advertising on %u.%u.%u.%u (%ls), for iPhones on this PC's Mobile hotspot\n",
                    b[0], b[1], b[2], b[3], pCurr->FriendlyName ? pCurr->FriendlyName : L"Mobile hotspot");
        }
        count++;
    }
    free(pAddresses);
    return count;
}
#else
static uint32_t mdns_get_default_ipv4(void)
{
    uint32_t addr = 0;
    int fd;
    struct sockaddr_in remote;
    struct sockaddr_in local;
    socklen_t local_len = sizeof(local);

    fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd == -1) {
        return 0;
    }

    memset(&remote, 0, sizeof(remote));
    remote.sin_family = AF_INET;
    remote.sin_port = htons(MDNS_PORT);
    inet_pton(AF_INET, MDNS_ADDR4, &remote.sin_addr);

    if (connect(fd, (struct sockaddr *) &remote, sizeof(remote)) == 0 &&
        getsockname(fd, (struct sockaddr *) &local, &local_len) == 0) {
        addr = local.sin_addr.s_addr;
    }

    CLOSESOCKET(fd);
    return addr;
}

/* Elsewhere, just the main network (as before) */
static int mdns_get_links4(mdns_link4_t *links, int max, int quiet)
{
    (void) quiet;
    memset(links, 0, sizeof(*links) * (size_t) max);
    links[0].addr = mdns_get_default_ipv4();
    return 1;
}
#endif

static int mdns_get_default_ipv6(unsigned char addr[16], unsigned int *scope_id)
{
#ifdef WIN32
    (void) addr;
    (void) scope_id;
    return 0;
#else
    struct ifaddrs *ifaddr = NULL;
    struct ifaddrs *ifa;
    int found = 0;

    memset(addr, 0, 16);
    *scope_id = 0;

    if (getifaddrs(&ifaddr) == -1) {
        return 0;
    }

    for (ifa = ifaddr; ifa; ifa = ifa->ifa_next) {
        struct sockaddr_in6 *sin6;
        unsigned char *bytes;

        if (!ifa->ifa_addr || ifa->ifa_addr->sa_family != AF_INET6 ||
            !(ifa->ifa_flags & IFF_UP) ||
            !(ifa->ifa_flags & IFF_MULTICAST) ||
            (ifa->ifa_flags & IFF_LOOPBACK)) {
            continue;
        }

        sin6 = (struct sockaddr_in6 *) ifa->ifa_addr;
        bytes = sin6->sin6_addr.s6_addr;
        if (IN6_IS_ADDR_UNSPECIFIED(&sin6->sin6_addr) ||
            IN6_IS_ADDR_LOOPBACK(&sin6->sin6_addr)) {
            continue;
        }

        if (!found || IN6_IS_ADDR_LINKLOCAL(&sin6->sin6_addr)) {
            memcpy(addr, bytes, 16);
            *scope_id = sin6->sin6_scope_id;
            if (!*scope_id) {
                *scope_id = if_nametoindex(ifa->ifa_name);
            }
            found = 1;
            if (IN6_IS_ADDR_LINKLOCAL(&sin6->sin6_addr)) {
                break;
            }
        }
    }

    freeifaddrs(ifaddr);
    if (!*scope_id) {
        memset(addr, 0, 16);
        return 0;
    }
    return found;
#endif
}

static int mdns_open_socket4(uint32_t iface_addr)
{
    int fd;
    int one = 1;
    unsigned char ttl = 255;
    unsigned char loop = 1;
    struct sockaddr_in addr;
    struct ip_mreq mreq;

    fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd == -1) {
        return -SOCKET_GET_ERROR();
    }

    setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, (const char *) &one, sizeof(one));
#ifdef SO_REUSEPORT
    setsockopt(fd, SOL_SOCKET, SO_REUSEPORT, (const char *) &one, sizeof(one));
#endif
    setsockopt(fd, IPPROTO_IP, IP_MULTICAST_TTL, (const char *) &ttl, sizeof(ttl));
    setsockopt(fd, IPPROTO_IP, IP_MULTICAST_LOOP, (const char *) &loop, sizeof(loop));

    if (iface_addr) {
        struct in_addr iface;
        iface.s_addr = iface_addr;
        setsockopt(fd, IPPROTO_IP, IP_MULTICAST_IF, (const char *) &iface, sizeof(iface));
    }

    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons(MDNS_PORT);
    addr.sin_addr.s_addr = htonl(INADDR_ANY);

    if (bind(fd, (struct sockaddr *) &addr, sizeof(addr)) == -1) {
        int error = SOCKET_GET_ERROR();
        CLOSESOCKET(fd);
        return -error;
    }

    memset(&mreq, 0, sizeof(mreq));
    inet_pton(AF_INET, MDNS_ADDR4, &mreq.imr_multiaddr);
    mreq.imr_interface.s_addr = iface_addr ? iface_addr : htonl(INADDR_ANY);
    if (setsockopt(fd, IPPROTO_IP, IP_ADD_MEMBERSHIP,
                   (const char *) &mreq, sizeof(mreq)) == -1) {
        int error = SOCKET_GET_ERROR();
        if (iface_addr) {
            mreq.imr_interface.s_addr = htonl(INADDR_ANY);
            if (setsockopt(fd, IPPROTO_IP, IP_ADD_MEMBERSHIP,
                           (const char *) &mreq, sizeof(mreq)) == 0) {
                return fd;
            }
        }
        CLOSESOCKET(fd);
        return -error;
    }

    return fd;
}

/* Listens for queries on one more network (a Mobile hotspot), or stops listening there */
static void mdns_membership4(int fd, uint32_t iface_addr, int join)
{
    struct ip_mreq mreq;

    if (fd == -1 || !iface_addr) {
        return;
    }
    memset(&mreq, 0, sizeof(mreq));
    inet_pton(AF_INET, MDNS_ADDR4, &mreq.imr_multiaddr);
    mreq.imr_interface.s_addr = iface_addr;
    setsockopt(fd, IPPROTO_IP, join ? IP_ADD_MEMBERSHIP : IP_DROP_MEMBERSHIP,
               (const char *) &mreq, sizeof(mreq));
}

static int mdns_open_socket6(unsigned int scope_id)
{
    int fd;
    int one = 1;
    int hops = 255;
    unsigned int loop = 1;
    struct sockaddr_in6 addr;
    struct ipv6_mreq mreq;

    if (!scope_id) {
        return -1;
    }

    fd = socket(AF_INET6, SOCK_DGRAM, 0);
    if (fd == -1) {
        return -SOCKET_GET_ERROR();
    }

    setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, (const char *) &one, sizeof(one));
#ifdef SO_REUSEPORT
    setsockopt(fd, SOL_SOCKET, SO_REUSEPORT, (const char *) &one, sizeof(one));
#endif
    setsockopt(fd, IPPROTO_IPV6, IPV6_V6ONLY, (const char *) &one, sizeof(one));
    setsockopt(fd, IPPROTO_IPV6, IPV6_MULTICAST_HOPS, (const char *) &hops, sizeof(hops));
    setsockopt(fd, IPPROTO_IPV6, IPV6_MULTICAST_LOOP, (const char *) &loop, sizeof(loop));
    setsockopt(fd, IPPROTO_IPV6, IPV6_MULTICAST_IF, (const char *) &scope_id, sizeof(scope_id));

    memset(&addr, 0, sizeof(addr));
    addr.sin6_family = AF_INET6;
    addr.sin6_port = htons(MDNS_PORT);
    addr.sin6_addr = in6addr_any;

    if (bind(fd, (struct sockaddr *) &addr, sizeof(addr)) == -1) {
        int error = SOCKET_GET_ERROR();
        CLOSESOCKET(fd);
        return -error;
    }

    memset(&mreq, 0, sizeof(mreq));
    inet_pton(AF_INET6, MDNS_ADDR6, &mreq.ipv6mr_multiaddr);
    mreq.ipv6mr_interface = scope_id;
    if (setsockopt(fd, IPPROTO_IPV6, IPV6_JOIN_GROUP,
                   (const char *) &mreq, sizeof(mreq)) == -1) {
        int error = SOCKET_GET_ERROR();
        CLOSESOCKET(fd);
        return -error;
    }

    return fd;
}

static int mdns_begin_response(mdns_packet_t *packet, size_t *count_pos)
{
    memset(packet, 0, sizeof(*packet));
    if (mdns_put_u16(packet, 0) ||
        mdns_put_u16(packet, DNS_FLAG_RESPONSE | DNS_FLAG_AUTHORITATIVE) ||
        mdns_put_u16(packet, 0)) {
        return -1;
    }
    *count_pos = packet->length;
    return mdns_put_u16(packet, 0) ||
           mdns_put_u16(packet, 0) ||
           mdns_put_u16(packet, 0);
}

static void mdns_finish_response(mdns_packet_t *packet, size_t count_pos,
                                 unsigned int answers)
{
    packet->bytes[count_pos] = (unsigned char) ((answers >> 8) & 0xff);
    packet->bytes[count_pos + 1] = (unsigned char) (answers & 0xff);
}

static int mdns_add_service_records(mdns_packet_t *packet,
                                    const mdnsd_service_t *service,
                                    const char *host_name, uint32_t ttl)
{
    if (!service->registered) {
        return 0;
    }

    return mdns_add_ptr(packet, "_services._dns-sd._udp.local", service->type, ttl) ||
           mdns_add_ptr(packet, service->type, service->name, ttl) ||
           mdns_add_srv(packet, service->name, service->port, host_name,
                        ttl ? MDNS_TTL_HOST : 0) ||
           mdns_add_txt(packet, service->name, &service->txt, ttl);
}

static int mdns_build_response(mdnsd_t *mdnsd, mdns_packet_t *packet,
                               const mdnsd_service_t *service,
                               int include_host, uint32_t ttl,
                               mdns_family_t family)
{
    size_t count_pos;
    unsigned int answers = 0;
    size_t before;

    if (mdns_begin_response(packet, &count_pos)) {
        return -1;
    }

    if (service && service->registered) {
        before = packet->length;
        if (mdns_add_service_records(packet, service, mdnsd->host_name, ttl)) {
            return -1;
        }
        if (packet->length != before) {
            answers += 4;
        }
    }

    if (include_host &&
        mdns_add_host_records(mdnsd, packet, ttl ? MDNS_TTL_HOST : 0,
                              &answers, family)) {
        return -1;
    }

    mdns_finish_response(packet, count_pos, answers);
    return answers ? 0 : -1;
}

static int mdns_build_combined_response(mdnsd_t *mdnsd, mdns_packet_t *packet,
                                        int include_airplay, int include_raop,
                                        int include_host, uint32_t ttl,
                                        mdns_family_t family)
{
    size_t count_pos;
    unsigned int answers = 0;
    size_t before;

    if (mdns_begin_response(packet, &count_pos)) {
        return -1;
    }

    if (include_airplay && mdnsd->airplay.registered) {
        before = packet->length;
        if (mdns_add_service_records(packet, &mdnsd->airplay, mdnsd->host_name,
                                     ttl)) {
            return -1;
        }
        if (packet->length != before) {
            answers += 4;
        }
    }

    if (include_raop && mdnsd->raop.registered) {
        before = packet->length;
        if (mdns_add_service_records(packet, &mdnsd->raop, mdnsd->host_name,
                                     ttl)) {
            return -1;
        }
        if (packet->length != before) {
            answers += 4;
        }
    }

    if (include_host &&
        mdns_add_host_records(mdnsd, packet, ttl ? MDNS_TTL_HOST : 0,
                              &answers, family)) {
        return -1;
    }

    if (!answers || packet->length > MDNS_COMBINED_PACKET_MAX) {
        return -1;
    }

    mdns_finish_response(packet, count_pos, answers);
    return 0;
}

static void mdns_send_packet4(mdnsd_t *mdnsd, const mdns_packet_t *packet,
                              const struct sockaddr_in *to)
{
    struct sockaddr_in addr;

    if (mdnsd->sock_fd4 == -1 || !packet->length) {
        return;
    }

    if (to) {
        addr = *to;
    } else {
        memset(&addr, 0, sizeof(addr));
        addr.sin_family = AF_INET;
        addr.sin_port = htons(MDNS_PORT);
        inet_pton(AF_INET, MDNS_ADDR4, &addr.sin_addr);
    }

    sendto(mdnsd->sock_fd4, (const char *) packet->bytes, (int) packet->length, 0,
           (struct sockaddr *) &addr, sizeof(addr));
}

static void mdns_send_packet6(mdnsd_t *mdnsd, const mdns_packet_t *packet,
                              const struct sockaddr_in6 *to)
{
    struct sockaddr_in6 addr;

    if (mdnsd->sock_fd6 == -1 || !packet->length) {
        return;
    }

    if (to) {
        addr = *to;
    } else {
        memset(&addr, 0, sizeof(addr));
        addr.sin6_family = AF_INET6;
        addr.sin6_port = htons(MDNS_PORT);
        addr.sin6_scope_id = mdnsd->ipv6_scope_id;
        inet_pton(AF_INET6, MDNS_ADDR6, &addr.sin6_addr);
    }

    sendto(mdnsd->sock_fd6, (const char *) packet->bytes, (int) packet->length, 0,
           (struct sockaddr *) &addr, sizeof(addr));
}

/* Sends what follows on links4[i]: multicast goes out on that network and the A
 * record holds this PC's address there */
static void mdns_use_link4_locked(mdnsd_t *mdnsd, int i)
{
    if (i < 0 || i >= mdnsd->nlinks4) {
        i = 0;
    }
    mdnsd->link4 = i;
    if (mdnsd->sock_fd4 != -1 && mdnsd->links4[i].addr) {
        struct in_addr iface;
        iface.s_addr = mdnsd->links4[i].addr;
        setsockopt(mdnsd->sock_fd4, IPPROTO_IP, IP_MULTICAST_IF,
                   (const char *) &iface, sizeof(iface));
    }
}

static void mdns_send_service_locked(mdnsd_t *mdnsd, const mdnsd_service_t *service,
                                     int include_host, uint32_t ttl,
                                     mdns_family_t family, const void *to)
{
    mdns_packet_t packet;

    if (service->registered &&
        !mdns_build_response(mdnsd, &packet, service, include_host, ttl,
                             family)) {
        if (family == MDNS_FAMILY_IPV4) {
            mdns_send_packet4(mdnsd, &packet, (const struct sockaddr_in *) to);
        } else {
            mdns_send_packet6(mdnsd, &packet, (const struct sockaddr_in6 *) to);
        }
    }
}

static void mdns_send_response_locked(mdnsd_t *mdnsd, int include_airplay,
                                      int include_raop, int include_host,
                                      uint32_t ttl, mdns_family_t family,
                                      const void *to)
{
    mdns_packet_t packet;

    if (include_airplay && include_raop &&
        mdnsd->airplay.registered && mdnsd->raop.registered &&
        !mdns_build_combined_response(mdnsd, &packet, include_airplay,
                                      include_raop, include_host, ttl,
                                      family)) {
        if (family == MDNS_FAMILY_IPV4) {
            mdns_send_packet4(mdnsd, &packet, (const struct sockaddr_in *) to);
        } else {
            mdns_send_packet6(mdnsd, &packet, (const struct sockaddr_in6 *) to);
        }
        return;
    }

    if (include_airplay) {
        mdns_send_service_locked(mdnsd, &mdnsd->airplay, include_host, ttl,
                                 family, to);
    }
    if (include_raop) {
        mdns_send_service_locked(mdnsd, &mdnsd->raop, include_host, ttl,
                                 family, to);
    }
    if (include_host && !include_airplay && !include_raop &&
        !mdns_build_response(mdnsd, &packet, NULL, 1, ttl, family)) {
        if (family == MDNS_FAMILY_IPV4) {
            mdns_send_packet4(mdnsd, &packet, (const struct sockaddr_in *) to);
        } else {
            mdns_send_packet6(mdnsd, &packet, (const struct sockaddr_in6 *) to);
        }
    }
}

static void mdns_announce_locked(mdnsd_t *mdnsd, uint32_t ttl)
{
    for (int i = 0; i < mdnsd->nlinks4 || i == 0; i++) {
        mdns_use_link4_locked(mdnsd, i);
        mdns_send_response_locked(mdnsd, 1, 1, 1, ttl, MDNS_FAMILY_IPV4, NULL);
    }
    mdns_use_link4_locked(mdnsd, 0);
    mdns_send_response_locked(mdnsd, 1, 1, 1, ttl, MDNS_FAMILY_IPV6, NULL);
}

/* Used on Windows: after looking again for networks to advertise on, takes the new
 * list, so a Mobile hotspot turned on after UxPlay started (or a new address on the
 * main network) is picked up. Listens on the new networks and stops on the ones gone;
 * the caller then announces. Returns 1 if anything changed. */
static inline int mdns_update_links4_locked(mdnsd_t *mdnsd, const mdns_link4_t *links, int count)
{
    int changed = (count != mdnsd->nlinks4);

    if (count < 1 || count > MDNS_MAX_LINKS4) {
        return 0;
    }
    for (int i = 0; i < count && !changed; i++) {
        changed = (links[i].addr != mdnsd->links4[i].addr || links[i].mask != mdnsd->links4[i].mask);
    }
    if (!changed) {
        return 0;
    }
    for (int i = 0; i < mdnsd->nlinks4; i++) {
        int kept = 0;
        for (int j = 0; j < count; j++) {
            kept |= (links[j].addr == mdnsd->links4[i].addr);
        }
        if (!kept) {
            mdns_membership4(mdnsd->sock_fd4, mdnsd->links4[i].addr, 0);
        }
    }
    for (int j = 0; j < count; j++) {
        int had = 0;
        for (int i = 0; i < mdnsd->nlinks4; i++) {
            had |= (links[j].addr == mdnsd->links4[i].addr);
        }
        if (!had) {
            mdns_membership4(mdnsd->sock_fd4, links[j].addr, 1);
        }
    }
    memcpy(mdnsd->links4, links, sizeof(*links) * (size_t) count);
    mdnsd->nlinks4 = count;
    mdns_use_link4_locked(mdnsd, 0);
    return 1;
}

static int mdns_question_matches_service(const mdnsd_t *mdnsd, const char *name,
                                         uint16_t type, int *airplay, int *raop,
                                         int *host)
{
    if (mdns_name_equals(name, "_services._dns-sd._udp.local") &&
        mdns_query_type_matches(type, DNS_TYPE_PTR)) {
        if (mdnsd->airplay.registered) {
            *airplay = 1;
        }
        if (mdnsd->raop.registered) {
            *raop = 1;
        }
        return *airplay || *raop;
    }

    if (mdnsd->airplay.registered &&
        ((mdns_name_equals(name, mdnsd->airplay.type) &&
          mdns_query_type_matches(type, DNS_TYPE_PTR)) ||
         (mdns_name_equals(name, mdnsd->airplay.name) &&
          (mdns_query_type_matches(type, DNS_TYPE_TXT) ||
           mdns_query_type_matches(type, DNS_TYPE_SRV))))) {
        *airplay = 1;
        *host = 1;
        return 1;
    }

    if (mdnsd->raop.registered &&
        ((mdns_name_equals(name, mdnsd->raop.type) &&
          mdns_query_type_matches(type, DNS_TYPE_PTR)) ||
         (mdns_name_equals(name, mdnsd->raop.name) &&
          (mdns_query_type_matches(type, DNS_TYPE_TXT) ||
           mdns_query_type_matches(type, DNS_TYPE_SRV))))) {
        *raop = 1;
        *host = 1;
        return 1;
    }

    if (mdns_name_equals(name, mdnsd->host_name) &&
        (mdns_query_type_matches(type, DNS_TYPE_A) ||
         mdns_query_type_matches(type, DNS_TYPE_AAAA))) {
        *host = 1;
        return 1;
    }

    return 0;
}

static void mdns_handle_query(mdnsd_t *mdnsd, const unsigned char *bytes,
                              size_t length, mdns_family_t family,
                              const void *from, int from_port)
{
    uint16_t flags;
    uint16_t qdcount;
    size_t offset = 12;
    int include_airplay = 0;
    int include_raop = 0;
    int include_host = 0;
    int unicast_reply = 0;

    if (length < 12) {
        return;
    }

    flags = mdns_read_u16(bytes + 2);
    if (flags & 0x8000) {
        return;
    }

    qdcount = mdns_read_u16(bytes + 4);

    MUTEX_LOCK(mdnsd->mutex);
    for (uint16_t i = 0; i < qdcount; i++) {
        char name[MDNSD_MAX_NAME];
        uint16_t type;
        uint16_t cls;

        if (mdns_read_name(bytes, length, &offset, name, sizeof(name)) ||
            offset + 4 > length) {
            break;
        }
        type = mdns_read_u16(bytes + offset);
        cls = mdns_read_u16(bytes + offset + 2);
        offset += 4;

        if (cls & DNS_UNICAST_RESPONSE) {
            unicast_reply = 1;
        }
        mdns_question_matches_service(mdnsd, name, type, &include_airplay,
                                      &include_raop, &include_host);
    }

    if (include_airplay || include_raop || include_host) {
        if (family == MDNS_FAMILY_IPV4 && from) {
            mdns_use_link4_locked(mdnsd, mdns_link4_for(mdnsd->links4, mdnsd->nlinks4,
                ((const struct sockaddr_in *) from)->sin_addr.s_addr));
        }
        mdns_send_response_locked(mdnsd, include_airplay, include_raop,
                                  include_host, MDNSD_TTL_SERVICE, family,
                                  NULL);
        if (from && (unicast_reply || from_port != htons(MDNS_PORT))) {
            mdns_send_response_locked(mdnsd, include_airplay, include_raop,
                                      include_host, MDNSD_TTL_SERVICE, family,
                                      from);
        }
        mdns_use_link4_locked(mdnsd, 0);
    }
    MUTEX_UNLOCK(mdnsd->mutex);
}

static THREAD_RETVAL mdns_thread(void *arg)
{
    mdnsd_t *mdnsd = arg;

    for (;;) {
        fd_set rfds;
        struct timeval tv;
        int fd4;
        int fd6;
        int max_fd = -1;
        int running;
        int ret;

        MUTEX_LOCK(mdnsd->mutex);
        fd4 = mdnsd->sock_fd4;
        fd6 = mdnsd->sock_fd6;
        running = mdnsd->running;
#ifdef _WIN32
        if (running && fd4 != -1 && time(NULL) - mdnsd->links4_checked >= MDNS_REFRESH_SECONDS) {
            mdns_link4_t links[MDNS_MAX_LINKS4];
            int count = mdns_get_links4(links, MDNS_MAX_LINKS4, 1);
            mdnsd->links4_checked = time(NULL);
            if (count > 0 && mdns_update_links4_locked(mdnsd, links, count)) {
                mdns_get_links4(links, MDNS_MAX_LINKS4, 0);  /* log the new list */
                mdns_announce_locked(mdnsd, MDNSD_TTL_SERVICE);
            }
        }
#endif
        MUTEX_UNLOCK(mdnsd->mutex);

        if (!running || (fd4 == -1 && fd6 == -1)) {
            break;
        }

        FD_ZERO(&rfds);
        if (fd4 != -1) {
            FD_SET(fd4, &rfds);
            max_fd = fd4;
        }
        if (fd6 != -1) {
            FD_SET(fd6, &rfds);
            if (fd6 > max_fd) {
                max_fd = fd6;
            }
        }
        tv.tv_sec = 0;
        tv.tv_usec = 250000;

        ret = select(max_fd + 1, &rfds, NULL, NULL, &tv);
        if (ret > 0 && fd4 != -1 && FD_ISSET(fd4, &rfds)) {
            unsigned char buffer[MDNS_MAX_PACKET];
            struct sockaddr_in from;
            socklen_t from_len = sizeof(from);
            int received = (int) recvfrom(fd4, (char *) buffer, sizeof(buffer), 0,
                                          (struct sockaddr *) &from, &from_len);
            if (received > 0) {
                mdns_handle_query(mdnsd, buffer, (size_t) received,
                                  MDNS_FAMILY_IPV4, &from, from.sin_port);
            }
        }
        if (ret > 0 && fd6 != -1 && FD_ISSET(fd6, &rfds)) {
            unsigned char buffer[MDNS_MAX_PACKET];
            struct sockaddr_in6 from;
            socklen_t from_len = sizeof(from);
            int received = (int) recvfrom(fd6, (char *) buffer, sizeof(buffer), 0,
                                          (struct sockaddr *) &from, &from_len);
            if (received > 0) {
                mdns_handle_query(mdnsd, buffer, (size_t) received,
                                  MDNS_FAMILY_IPV6, &from, from.sin6_port);
            }
        }
    }

    return NULL;
}

int mdnsd_txt_add(mdnsd_txt_t *txt, const char *key, const char *value)
{
    char item[256];
    int length = snprintf(item, sizeof(item), "%s=%s", key, value ? value : "");

    if (length < 0 || length > 255 ||
        txt->length + length + 1 > (int) sizeof(txt->bytes)) {
        return -1;
    }

    txt->bytes[txt->length++] = (unsigned char) length;
    memcpy(txt->bytes + txt->length, item, (size_t) length);
    txt->length += length;
    return 0;
}

mdnsd_t *mdnsd_init(const char *host_name)
{
    mdnsd_t *mdnsd = calloc(1, sizeof(*mdnsd));
    if (!mdnsd) {
        return NULL;
    }

    snprintf(mdnsd->host_name, sizeof(mdnsd->host_name), "%s",
             host_name && *host_name ? host_name : "UxPlay.local");
    mdnsd->sock_fd4 = -1;
    mdnsd->sock_fd6 = -1;
    MUTEX_CREATE(mdnsd->mutex);
    return mdnsd;
}

void mdnsd_destroy(mdnsd_t *mdnsd)
{
    if (!mdnsd) {
        return;
    }

    mdnsd_stop(mdnsd);
    MUTEX_DESTROY(mdnsd->mutex);
    free(mdnsd);
}

int mdnsd_start(mdnsd_t *mdnsd)
{
    if (!mdnsd) {
        return -1;
    }

    MUTEX_LOCK(mdnsd->mutex);
    if (mdnsd->running) {
        MUTEX_UNLOCK(mdnsd->mutex);
        return 0;
    }

    int error4 = 0;
    int error6 = 0;

    mdnsd->nlinks4 = mdns_get_links4(mdnsd->links4, MDNS_MAX_LINKS4, 0);
    if (mdnsd->nlinks4 < 1) {
        memset(mdnsd->links4, 0, sizeof(mdnsd->links4));
        mdnsd->nlinks4 = 1;
    }
    mdnsd->link4 = 0;
    mdnsd->links4_checked = time(NULL);
    mdns_get_default_ipv6(mdnsd->ipv6_addr, &mdnsd->ipv6_scope_id);

    mdnsd->sock_fd4 = mdns_open_socket4(mdnsd->links4[0].addr);
    if (mdnsd->sock_fd4 < 0) {
        error4 = mdnsd->sock_fd4;
        mdnsd->sock_fd4 = -1;
    }
    for (int i = 1; i < mdnsd->nlinks4; i++) {
        mdns_membership4(mdnsd->sock_fd4, mdnsd->links4[i].addr, 1);
    }

    mdnsd->sock_fd6 = mdns_open_socket6(mdnsd->ipv6_scope_id);
    if (mdnsd->sock_fd6 < 0) {
        error6 = mdnsd->sock_fd6;
        mdnsd->sock_fd6 = -1;
    }

    if (mdnsd->sock_fd4 == -1 && mdnsd->sock_fd6 == -1) {
        MUTEX_UNLOCK(mdnsd->mutex);
        return error4 ? error4 : error6;
    }

    mdnsd->running = 1;
    THREAD_CREATE(mdnsd->thread, mdns_thread, mdnsd);
    if (!mdnsd->thread) {
        if (mdnsd->sock_fd4 != -1) {
            CLOSESOCKET(mdnsd->sock_fd4);
            mdnsd->sock_fd4 = -1;
        }
        if (mdnsd->sock_fd6 != -1) {
            CLOSESOCKET(mdnsd->sock_fd6);
            mdnsd->sock_fd6 = -1;
        }
        mdnsd->running = 0;
        MUTEX_UNLOCK(mdnsd->mutex);
        return -1;
    }

    MUTEX_UNLOCK(mdnsd->mutex);
    return 0;
}

void mdnsd_stop(mdnsd_t *mdnsd)
{
    int fd4 = -1;
    int fd6 = -1;
    thread_handle_t thread = 0;

    if (!mdnsd) {
        return;
    }

    MUTEX_LOCK(mdnsd->mutex);
    if (mdnsd->running) {
        mdnsd->running = 0;
        fd4 = mdnsd->sock_fd4;
        fd6 = mdnsd->sock_fd6;
        thread = mdnsd->thread;
        mdnsd->sock_fd4 = -1;
        mdnsd->sock_fd6 = -1;
        mdnsd->thread = 0;
    }
    MUTEX_UNLOCK(mdnsd->mutex);

    if (fd4 != -1) {
        CLOSESOCKET(fd4);
    }
    if (fd6 != -1) {
        CLOSESOCKET(fd6);
    }
    if (thread) {
        THREAD_JOIN(thread);
    }
}

void mdnsd_set_services(mdnsd_t *mdnsd, const mdnsd_service_t *airplay,
                        const mdnsd_service_t *raop)
{
    if (!mdnsd) {
        return;
    }

    MUTEX_LOCK(mdnsd->mutex);
    if (airplay) {
        mdnsd->airplay = *airplay;
    } else {
        memset(&mdnsd->airplay, 0, sizeof(mdnsd->airplay));
    }
    if (raop) {
        mdnsd->raop = *raop;
    } else {
        memset(&mdnsd->raop, 0, sizeof(mdnsd->raop));
    }
    MUTEX_UNLOCK(mdnsd->mutex);
}

void mdnsd_announce(mdnsd_t *mdnsd, uint32_t ttl)
{
    if (!mdnsd) {
        return;
    }

    MUTEX_LOCK(mdnsd->mutex);
    mdns_announce_locked(mdnsd, ttl);
    MUTEX_UNLOCK(mdnsd->mutex);
}

void mdnsd_goodbye(mdnsd_t *mdnsd, const mdnsd_service_t *service)
{
    if (!mdnsd || !service) {
        return;
    }

    MUTEX_LOCK(mdnsd->mutex);
    for (int i = 0; i < mdnsd->nlinks4 || i == 0; i++) {
        mdns_use_link4_locked(mdnsd, i);
        mdns_send_service_locked(mdnsd, service, 0, 0, MDNS_FAMILY_IPV4, NULL);
    }
    mdns_use_link4_locked(mdnsd, 0);
    mdns_send_service_locked(mdnsd, service, 0, 0, MDNS_FAMILY_IPV6, NULL);
    MUTEX_UNLOCK(mdnsd->mutex);
}
